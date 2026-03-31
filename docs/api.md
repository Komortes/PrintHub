# PrintHub API Reference

PrintHub is a local print-job service running on `http://localhost:<port>` (default **5217**).

---

## Authentication

| Request origin | Auth required |
|---|---|
| Same machine (localhost) | None — all endpoints open |
| External machine | `Authorization: Bearer <key>` preferred, custom API key header also supported |

Preferred auth format:

```http
Authorization: Bearer my-secret-key
```

Legacy compatibility header:

```http
X-PrintHub-Api-Key: my-secret-key
```

The custom header name remains configurable in settings (`apiKeyHeaderName`). Default: `X-PrintHub-Api-Key`.

If the API key is not yet configured, external requests receive **503 Service Unavailable** until onboarding is completed.

---

## Endpoints

### Health

#### `GET /health`

Returns service liveness. No auth required.

**Response 200**
```json
{
  "status": "healthy",
  "service": "PrintHub",
  "timestamp": "2025-01-15T12:00:00Z"
}
```

---

### Setup & Onboarding

#### `GET /settings/setup-status`

Returns whether the service is configured. Used by the UI on first launch.

**Response 200**
```json
{
  "isOnboardingRequired": true,
  "hasApiKey": false,
  "hasDefaultPrinter": false,
  "printers": []
}
```

| Field | Description |
|---|---|
| `isOnboardingRequired` | `true` when no API key is set — external access is not yet possible |
| `hasApiKey` | API key has been configured |
| `hasDefaultPrinter` | At least one registered printer is marked as default |
| `printers` | Array of registered printers with live status (see [Printer object](#printer-object)) |

#### `POST /settings/onboarding`

Sets the API key on first run. Allowed without auth (only when `isOnboardingRequired` is `true`).

**Request**
```json
{
  "apiKey": "my-secret-key"
}
```

**Response 200** — same as `GET /settings/setup-status`.

**Response 422** — `apiKey` is empty.

---

### Settings

#### `GET /settings`

Returns current configuration. Requires auth for external requests.

**Response 200**
```json
{
  "serviceName": "PrintHub",
  "port": 5217,
  "apiKeyHeaderName": "X-PrintHub-Api-Key",
  "apiKey": "my-secret-key",
  "defaultPrinterName": "Zebra ZT410",
  "storageDirectory": null,
  "maxUploadSizeBytes": 10485760
}
```

#### `PUT /settings`

Updates configuration. All fields must be provided (full replacement).

**Request**
```json
{
  "serviceName": "PrintHub",
  "port": 5217,
  "apiKeyHeaderName": "X-PrintHub-Api-Key",
  "apiKey": "new-key",
  "defaultPrinterName": "Zebra ZT410",
  "storageDirectory": null,
  "maxUploadSizeBytes": 10485760
}
```

**Response 200** — updated settings object.

**Response 400** — validation error (e.g. invalid port, empty required field).

#### `GET /settings/auto-start`

Returns whether PrintHub is registered to start automatically on system boot.

**Response 200**
```json
{
  "isSupported": true,
  "isEnabled": false,
  "provider": "WindowsRegistry",
  "entryPath": null
}
```

#### `PUT /settings/auto-start`

Enables or disables auto-start.

**Request**
```json
{
  "enabled": true
}
```

**Response 200** — same as `GET /settings/auto-start`.

---

### Printers

#### Printer object

```json
{
  "id": "ZebraZT410",
  "name": "Zebra ZT410",
  "isDefault": true,
  "status": "ready"
}
```

| Field | Values |
|---|---|
| `status` | `ready` · `busy` · `offline` · `error` · `unknown` |

#### `GET /printers`

Lists all printers in the registry with their live OS status.

**Response 200** — array of printer objects.

```bash
curl http://localhost:5217/printers \
  -H "X-PrintHub-Api-Key: my-secret-key"
```

#### `GET /printers/discover`

Lists OS printers that are **not yet** in the registry (available to add).

**Response 200** — array of printer objects.

#### `POST /printers`

Adds an OS printer to the registry. The `id` must match a printer returned by `/printers/discover`.

**Request**
```json
{
  "id": "ZebraZT410"
}
```

**Response 200** — the added printer object.

**Response 404** — printer not found in OS.

#### `DELETE /printers/{printerId}`

Removes a printer from the registry.

**Response 204** — success.

**Response 404** — printer not in registry.

#### `PUT /printers/{printerId}/default`

Sets a registered printer as the default (used when `printerName` is omitted in print jobs).

**Response 200** — the updated printer object.

**Response 404** — printer not in registry.

#### `POST /printers/{printerId}/test-print`

Sends a test PDF page to the printer.

**Response 201** — print job object (see [Print Job object](#print-job-object)).

**Response 404** — printer not in registry.

---

### Print Jobs

#### Print Job object

```json
{
  "jobId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "printerName": "Zebra ZT410",
  "status": "completed",
  "createdAt": "2025-01-15T12:00:00Z",
  "startedAt": "2025-01-15T12:00:01Z",
  "completedAt": "2025-01-15T12:00:02Z",
  "copies": 1,
  "format": "pdf",
  "errorMessage": null
}
```

| `status` | Meaning |
|---|---|
| `pending` | Queued, not yet sent to printer |
| `processing` | Being sent to printer |
| `completed` | Successfully printed |
| `failed` | Printer rejected the job — see `errorMessage` |
| `canceled` | Canceled before processing |

#### `POST /print-jobs`

Creates and queues a print job. Supports two content types.

---

**Option 1 — JSON with URL**

```bash
curl -X POST http://localhost:5217/print-jobs \
  -H "X-PrintHub-Api-Key: my-secret-key" \
  -H "Content-Type: application/json" \
  -d '{
    "printerName": "Zebra ZT410",
    "copies": 1,
    "document": {
      "type": "url",
      "format": "pdf",
      "url": "https://example.com/label.pdf"
    }
  }'
```

**Option 2 — JSON with Base64 data**

```bash
curl -X POST http://localhost:5217/print-jobs \
  -H "X-PrintHub-Api-Key: my-secret-key" \
  -H "Content-Type: application/json" \
  -d '{
    "printerName": "Zebra ZT410",
    "copies": 2,
    "document": {
      "type": "base64",
      "format": "pdf",
      "data": "<base64-encoded-pdf>",
      "fileName": "label.pdf"
    }
  }'
```

**Option 3 — Multipart file upload**

```bash
curl -X POST http://localhost:5217/print-jobs \
  -H "X-PrintHub-Api-Key: my-secret-key" \
  -F "file=@/path/to/label.pdf" \
  -F "printerName=Zebra ZT410" \
  -F "copies=1"
```

---

**Request fields (JSON)**

| Field | Required | Description |
|---|---|---|
| `printerName` | No | Target printer name. Omit to use the default printer. |
| `copies` | No | Number of copies. Default: `1`. |
| `document.type` | Yes | `url` · `base64` · `upload` (multipart only) |
| `document.format` | Yes | `pdf` |
| `document.url` | When `type=url` | HTTP(S) URL to download the PDF from |
| `document.data` | When `type=base64` | Base64-encoded PDF content |
| `document.fileName` | No | Original file name (informational) |

**Response 201** — created print job object.

**Response 400** — missing or invalid fields, unsupported content type, file too large, non-PDF file.

---

#### `GET /print-jobs`

Lists print jobs. Supports optional filtering.

| Query param | Description |
|---|---|
| `status` | Filter by status: `pending`, `processing`, `completed`, `failed`, `canceled` |
| `activeOnly=true` | Only `pending` and `processing` jobs |
| `limit=N` | Return at most N jobs |

```bash
# Last 20 completed jobs
curl "http://localhost:5217/print-jobs?status=completed&limit=20" \
  -H "X-PrintHub-Api-Key: my-secret-key"
```

**Response 200** — array of print job objects.

#### `GET /print-jobs/{jobId}`

Returns a single print job by ID.

**Response 200** — print job object.

**Response 404** — job not found.

---

## Error responses

All errors follow [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457):

```json
{
  "title": "Invalid print job request",
  "detail": "Only PDF documents are supported in the current version.",
  "status": 400
}
```

| Status | When |
|---|---|
| 400 | Request validation failed |
| 401 | Wrong or missing API key |
| 404 | Resource not found |
| 422 | Semantic validation failed (e.g. empty onboarding key) |
| 503 | API key not yet configured — complete onboarding first |
