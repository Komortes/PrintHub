import AppKit
import Foundation

@main
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private let statusMenuItem = NSMenuItem(title: "Status: checking...", action: nil, keyEquivalent: "")
    private var statusTimer: Timer?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.title = "⎙ PrintHub"

        let menu = NSMenu()
        menu.addItem(statusMenuItem)
        menu.addItem(NSMenuItem.separator())

        menu.addItem(createMenuItem("Open Dashboard", #selector(openDashboard), "o"))
        menu.addItem(createMenuItem("Start in Background", #selector(startInBackground), "s"))
        menu.addItem(createMenuItem("Stop PrintHub", #selector(stopPrintHub), "x"))
        menu.addItem(NSMenuItem.separator())
        menu.addItem(createMenuItem("Open Runtime Folder", #selector(openRuntimeFolder), "r"))
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

    @objc private func startInBackground() {
        runScript(named: "run-printhub.sh", openBrowser: false)
    }

    @objc private func stopPrintHub() {
        runScript(named: "stop-printhub.sh", openBrowser: false)
    }

    @objc private func openRuntimeFolder() {
        let runtimeURL = resolvePrintHubHome()
        try? FileManager.default.createDirectory(at: runtimeURL, withIntermediateDirectories: true)
        NSWorkspace.shared.open(runtimeURL)
    }

    @objc private func quitTray() {
        NSApp.terminate(nil)
    }

    private func createMenuItem(_ title: String, _ action: Selector, _ keyEquivalent: String) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: keyEquivalent)
        item.target = self
        return item
    }

    private func runScript(named scriptName: String, openBrowser: Bool) {
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
        process.environment = environment

        do {
            try process.run()
        } catch {
            showAlert(title: "PrintHub Tray", message: "Failed to run \(scriptName): \(error.localizedDescription)")
        }
    }

    private func refreshStatus() {
        statusMenuItem.title = "Status: checking..."

        guard let healthURL = URL(string: "\(resolveBaseURL())/health") else {
            statusMenuItem.title = "Status: invalid URL"
            return
        }

        let task = URLSession.shared.dataTask(with: healthURL) { [weak self] _, response, error in
            DispatchQueue.main.async {
                if let error {
                    self?.statusMenuItem.title = "Status: stopped (\(error.localizedDescription))"
                    return
                }

                guard let httpResponse = response as? HTTPURLResponse else {
                    self?.statusMenuItem.title = "Status: unknown"
                    return
                }

                self?.statusMenuItem.title = httpResponse.statusCode == 200
                    ? "Status: running"
                    : "Status: unavailable (\(httpResponse.statusCode))"
            }
        }

        task.resume()
    }

    private func resolveBaseURL() -> String {
        if let explicitUrl = ProcessInfo.processInfo.environment["PRINTHUB_URL"], !explicitUrl.isEmpty {
            return explicitUrl
        }

        let settingsURL = resolvePrintHubHome()
            .appendingPathComponent("data", isDirectory: true)
            .appendingPathComponent("settings.json")

        if let data = try? Data(contentsOf: settingsURL),
           let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           let port = json["port"] as? NSNumber {
            return "http://127.0.0.1:\(port)"
        }

        return "http://127.0.0.1:5051"
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
