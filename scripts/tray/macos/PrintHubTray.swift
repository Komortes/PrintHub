import AppKit
import Foundation

private struct PrintHubSettings {
    let port: Int
    let apiKey: String?
    let apiKeyHeaderName: String?
}

private struct PrintQueueStatus: Decodable {
    let isPaused: Bool
    let queuedCount: Int
}

private struct PrintJobSummary: Decodable {
    let status: String
}

@main
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private let statusMenuItem = NSMenuItem(title: "Status: checking...", action: nil, keyEquivalent: "")
    private let queueMenuItem = NSMenuItem(title: "Queue: checking...", action: nil, keyEquivalent: "")
    private var statusTimer: Timer?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.title = "⎙ PrintHub"

        let menu = NSMenu()
        menu.addItem(statusMenuItem)
        menu.addItem(queueMenuItem)
        menu.addItem(NSMenuItem.separator())

        menu.addItem(createMenuItem("Open Dashboard", #selector(openDashboard), "o"))
        menu.addItem(createMenuItem("Open Printers", #selector(openPrinters), "p"))
        menu.addItem(createMenuItem("Open Settings", #selector(openSettings), ","))
        menu.addItem(createMenuItem("Start in Background", #selector(startInBackground), "s"))
        menu.addItem(createMenuItem("Restart PrintHub", #selector(restartPrintHub), "r"))
        menu.addItem(createMenuItem("Stop PrintHub", #selector(stopPrintHub), "x"))
        menu.addItem(NSMenuItem.separator())
        menu.addItem(createMenuItem("Open Runtime Folder", #selector(openRuntimeFolder), "f"))
        menu.addItem(createMenuItem("Open Logs Folder", #selector(openLogsFolder), "l"))
        menu.addItem(NSMenuItem.separator())
        menu.addItem(createMenuItem("Quit Tray", #selector(quitTray), "q"))

        statusItem.menu = menu

        refreshStatus()
        statusTimer = Timer.scheduledTimer(withTimeInterval: 5, repeats: true) { [weak self] _ in
            self?.refreshStatus()
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        statusTimer?.invalidate()
    }

    @objc private func openDashboard() {
        runScript(named: "run-printhub.sh", openBrowser: true)
    }

    @objc private func openPrinters() {
        runScript(named: "run-printhub.sh", openBrowser: true, openUrlSuffix: "#printers")
    }

    @objc private func openSettings() {
        runScript(named: "run-printhub.sh", openBrowser: true, openUrlSuffix: "#settings")
    }

    @objc private func startInBackground() {
        runScript(named: "run-printhub.sh", openBrowser: false)
    }

    @objc private func stopPrintHub() {
        runScript(named: "stop-printhub.sh", openBrowser: false)
    }

    @objc private func restartPrintHub() {
        runScript(named: "stop-printhub.sh", openBrowser: false)

        DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) { [weak self] in
            self?.runScript(named: "run-printhub.sh", openBrowser: false)
        }
    }

    @objc private func openRuntimeFolder() {
        let runtimeURL = resolvePrintHubHome()
        try? FileManager.default.createDirectory(at: runtimeURL, withIntermediateDirectories: true)
        NSWorkspace.shared.open(runtimeURL)
    }

    @objc private func openLogsFolder() {
        let logsURL = resolvePrintHubHome()
            .appendingPathComponent("data", isDirectory: true)
            .appendingPathComponent("logs", isDirectory: true)
        try? FileManager.default.createDirectory(at: logsURL, withIntermediateDirectories: true)
        NSWorkspace.shared.open(logsURL)
    }

    @objc private func quitTray() {
        NSApp.terminate(nil)
    }

    private func createMenuItem(_ title: String, _ action: Selector, _ keyEquivalent: String) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: keyEquivalent)
        item.target = self
        return item
    }

    private func runScript(named scriptName: String, openBrowser: Bool, openUrlSuffix: String? = nil) {
        guard let payloadDirectory = resolveAppPayloadDirectory() else {
            showAlert(title: "PrintHub Tray", message: "Could not find the PrintHub launcher scripts.")
            return
        }

        let scriptURL = payloadDirectory.appendingPathComponent(scriptName)

        guard FileManager.default.isExecutableFile(atPath: scriptURL.path) else {
            showAlert(title: "PrintHub Tray", message: "Launcher script is missing: \(scriptURL.path)")
            return
        }

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/bash")
        process.arguments = [scriptURL.path]
        process.currentDirectoryURL = payloadDirectory

        var environment = ProcessInfo.processInfo.environment
        environment["PRINTHUB_OPEN_BROWSER"] = openBrowser ? "true" : "false"
        if let openUrlSuffix, !openUrlSuffix.isEmpty {
            environment["PRINTHUB_OPEN_URL_SUFFIX"] = openUrlSuffix
        } else {
            environment.removeValue(forKey: "PRINTHUB_OPEN_URL_SUFFIX")
        }
        process.environment = environment

        do {
            try process.run()
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.0) { [weak self] in
                self?.refreshStatus()
            }
        } catch {
            showAlert(title: "PrintHub Tray", message: "Failed to run \(scriptName): \(error.localizedDescription)")
        }
    }

    private func refreshStatus() {
        statusMenuItem.title = "Status: checking..."
        queueMenuItem.title = "Queue: checking..."

        guard let healthURL = URL(string: "\(resolveBaseURL())/health") else {
            statusMenuItem.title = "Status: invalid URL"
            queueMenuItem.title = "Queue: invalid URL"
            return
        }

        let task = URLSession.shared.dataTask(with: healthURL) { [weak self] _, response, error in
            DispatchQueue.main.async {
                if let error {
                    self?.statusMenuItem.title = "Status: stopped (\(error.localizedDescription))"
                    self?.queueMenuItem.title = "Queue: unavailable"
                    return
                }

                guard let httpResponse = response as? HTTPURLResponse else {
                    self?.statusMenuItem.title = "Status: unknown"
                    self?.queueMenuItem.title = "Queue: unknown"
                    return
                }

                guard httpResponse.statusCode == 200 else {
                    self?.statusMenuItem.title = "Status: unavailable (\(httpResponse.statusCode))"
                    self?.queueMenuItem.title = "Queue: unavailable"
                    return
                }

                self?.statusMenuItem.title = "Status: running"
                self?.refreshQueueSummary()
            }
        }

        task.resume()
    }

    private func refreshQueueSummary() {
        guard let settings = resolveSettings(),
              let queueURL = URL(string: "\(resolveBaseURL())/print-jobs/queue"),
              let activeJobsURL = URL(string: "\(resolveBaseURL())/print-jobs?activeOnly=true&limit=100") else {
            queueMenuItem.title = "Queue: auth unavailable"
            return
        }

        let group = DispatchGroup()
        var queueStatus: PrintQueueStatus?
        var activeJobs: [PrintJobSummary] = []
        var queueError: Error?
        var jobsError: Error?

        group.enter()
        performJsonRequest(url: queueURL, settings: settings) { (result: Result<PrintQueueStatus, Error>) in
            defer { group.leave() }
            switch result {
            case .success(let value):
                queueStatus = value
            case .failure(let error):
                queueError = error
            }
        }

        group.enter()
        performJsonRequest(url: activeJobsURL, settings: settings) { (result: Result<[PrintJobSummary], Error>) in
            defer { group.leave() }
            switch result {
            case .success(let value):
                activeJobs = value
            case .failure(let error):
                jobsError = error
            }
        }

        group.notify(queue: .main) { [weak self] in
            guard let self else { return }

            if queueError != nil || jobsError != nil {
                self.queueMenuItem.title = "Queue: limited status"
                return
            }

            guard let queueStatus else {
                self.queueMenuItem.title = "Queue: unavailable"
                return
            }

            let processingCount = activeJobs.filter { $0.status.lowercased() == "processing" }.count
            let pendingCount = activeJobs.filter { $0.status.lowercased() == "pending" }.count
            let stateLabel = queueStatus.isPaused ? "paused" : "active"

            self.queueMenuItem.title = "Queue: \(stateLabel), queued \(queueStatus.queuedCount), pending \(pendingCount), printing \(processingCount)"
            self.statusItem.button?.title = processingCount > 0
                ? "⎙ PrintHub \(processingCount)"
                : "⎙ PrintHub"
        }
    }

    private func performJsonRequest<T: Decodable>(
        url: URL,
        settings: PrintHubSettings,
        completion: @escaping (Result<T, Error>) -> Void
    ) {
        var request = URLRequest(url: url)
        if let apiKey = settings.apiKey, !apiKey.isEmpty {
            request.setValue(apiKey, forHTTPHeaderField: settings.apiKeyHeaderName ?? "X-PrintHub-Api-Key")
        }

        URLSession.shared.dataTask(with: request) { data, response, error in
            if let error {
                completion(.failure(error))
                return
            }

            guard let httpResponse = response as? HTTPURLResponse, httpResponse.statusCode == 200 else {
                completion(.failure(NSError(domain: "PrintHubTray", code: 1)))
                return
            }

            guard let data else {
                completion(.failure(NSError(domain: "PrintHubTray", code: 2)))
                return
            }

            do {
                let decoded = try JSONDecoder().decode(T.self, from: data)
                completion(.success(decoded))
            } catch {
                completion(.failure(error))
            }
        }.resume()
    }

    private func resolveBaseURL() -> String {
        if let explicitUrl = ProcessInfo.processInfo.environment["PRINTHUB_URL"], !explicitUrl.isEmpty {
            return explicitUrl
        }

        if let settings = resolveSettings() {
            return "http://127.0.0.1:\(settings.port)"
        }

        return "http://127.0.0.1:5051"
    }

    private func resolveSettings() -> PrintHubSettings? {
        let settingsURL = resolvePrintHubHome()
            .appendingPathComponent("data", isDirectory: true)
            .appendingPathComponent("settings.json")

        guard let data = try? Data(contentsOf: settingsURL),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return nil
        }

        let port = (json["port"] as? NSNumber)?.intValue ?? 5051
        let apiKey = json["apiKey"] as? String
        let apiKeyHeaderName = json["apiKeyHeaderName"] as? String

        return PrintHubSettings(
            port: port,
            apiKey: apiKey,
            apiKeyHeaderName: apiKeyHeaderName)
    }

    private func resolvePrintHubHome() -> URL {
        if let explicitHome = ProcessInfo.processInfo.environment["PRINTHUB_HOME"], !explicitHome.isEmpty {
            return URL(fileURLWithPath: explicitHome, isDirectory: true)
        }

        return FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library", isDirectory: true)
            .appendingPathComponent("Application Support", isDirectory: true)
            .appendingPathComponent("PrintHub", isDirectory: true)
    }

    private func resolveAppPayloadDirectory() -> URL? {
        let fileManager = FileManager.default
        let bundleURL = Bundle.main.bundleURL
        let parentDirectory = bundleURL.deletingLastPathComponent()

        let candidates: [URL] = [
            parentDirectory
                .appendingPathComponent("PrintHub.app", isDirectory: true)
                .appendingPathComponent("Contents", isDirectory: true)
                .appendingPathComponent("Resources", isDirectory: true)
                .appendingPathComponent("app", isDirectory: true),
            parentDirectory,
            Bundle.main.resourceURL?.appendingPathComponent("app", isDirectory: true),
            Bundle.main.resourceURL
        ]
        .compactMap { $0 }

        for candidate in candidates {
            let launcher = candidate.appendingPathComponent("run-printhub.sh")
            if fileManager.isExecutableFile(atPath: launcher.path) {
                return candidate
            }
        }

        return nil
    }

    private func showAlert(title: String, message: String) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = title
        alert.informativeText = message
        alert.runModal()
    }
}
