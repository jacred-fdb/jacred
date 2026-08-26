---
title: GET /api/v1.0/conf
description: Identity and API key probe — response combinations for external apps
tags:
  - api
  - reference
---
# GET `/api/v1.0/conf`

Public discovery endpoint. Always HTTP 200. Key may be passed as `?apikey=…`, `X-Api-Key`, or `Authorization: Bearer …`.

## Response fields

| Field | Meaning |
| --- | --- |
| `jacred` | Always `true` — this host is JacRed |
| `configured` | `true` when the server has a non-blank `apikey` |
| `apikey` | `true` if no key is required **or** the provided key matches |
| `version` | Build version string — same as `GET /version` → `version` (`VersionInfo.Version`, e.g. `3.7.1-next+726f1544`) |

## Combinations

### 1. No API key on server

Client sends anything (or nothing):

```json
{
  "jacred": true,
  "configured": false,
  "apikey": true,
  "version": "3.7.1-next+726f1544"
}
```

Meaning: JacRed, no key required, access OK.

### 2. API key configured + valid key

Client sends a matching key:

```json
{
  "jacred": true,
  "configured": true,
  "apikey": true,
  "version": "3.7.1-next+726f1544"
}
```

Meaning: JacRed, key is required, provided key is valid.

### 3. API key configured + missing key

Client sends no key:

```json
{
  "jacred": true,
  "configured": true,
  "apikey": false,
  "version": "3.7.1-next+726f1544"
}
```

Meaning: JacRed, key is required, key missing/invalid.

### 4. API key configured + wrong key

Client sends a non-matching key:

```json
{
  "jacred": true,
  "configured": true,
  "apikey": false,
  "version": "3.7.1-next+726f1544"
}
```

Same shape as #3 — missing and wrong both yield `apikey: false`.

## Decision table

| `jacred` | `configured` | `apikey` | Conclusion |
| --- | --- | --- | --- |
| `true` | `false` | `true` | JacRed, open (no key) |
| `true` | `true` | `true` | JacRed, key valid |
| `true` | `true` | `false` | JacRed, key required but missing/invalid |
| not `true` / non-JSON / error | — | — | Not JacRed (or unreachable) |

`version` is always present (same value for all cases above).

Unused: `configured: false` with `apikey: false` (open hosts always accept).
