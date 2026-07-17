# Privacy model

MCP Track Tokens is designed so **activity metadata is useful without storing prompt or response bodies**.

## Defaults

Defined in domain (`PromptPrivacy`) and `Tracking` options:

| Flag | Default | Effect |
| --- | --- | --- |
| `StorePromptContent` | `false` | Do not persist raw prompts |
| `StoreResponseContent` | `false` | Do not persist model responses |
| `EnablePromptHashing` | `true` (server) | Allow SHA-256 hashes for correlation |
| Extension `enablePromptHashing` | `false` | Extension does not hash unless enabled |
| Hooks store/hash env | `false` | Hooks send length; hash only if opted in |

## What is stored by default

For prompt-related activity events:

- Event type, timestamps, session/project linkage when known
- Repository path / remote URL when resolved
- **Prompt length**
- Optional **prompt hash** (server/hooks/extension policy)
- Bounded **metadata** (no raw prompt field)

Not stored by default:

- Prompt text
- Completion / response text
- Chat message bodies

## When content storage is enabled

1. Set `Tracking:StorePromptContent` / `MCP_TRACK_TOKENS_STORE_PROMPT_CONTENT=true`.
2. Ensure encryption is configured (`EncryptionKeyPath`, default `~/.mcp-track-tokens/encryption.key`).
3. Server persists `PromptContentEncrypted` only when encryption is available; otherwise content is discarded after deriving length/hash.

Treat enabled content storage as a deliberate compliance decision (retention, access control, key backup).

## Hashing

Server-side hash format uses a stable composition including session context (see `PromptPrivacy` / ingestion service). Hooks may hash `{salt}:{text}` when enabled. Hashes support correlation, not recovery of plaintext.

## Client sanitization

- Hooks: `privacySanitize` + diagnostics redaction of `prompt` / `text` / `content` / `messages` / etc.
- Hooks never put raw prompt strings into event `metadata`.
- Extension defaults match “length only” for `@track`.

## Network and auth

- Localhost bind by default.
- API requires Bearer API key.
- Dashboard stores the key in browser `localStorage` (`mcp-track-tokens-api-key`) — appropriate for single-user localhost, not shared kiosks.

## Operator checklist

- Leave content storage off unless required.
- Rotate API keys if leaked (`create-api-key`, revoke old keys in DB/admin flows).
- Exclude `~/.mcp-track-tokens/` from public backups or encrypt backups.
- Do not commit `.env`, API keys, or `encryption.key`.
