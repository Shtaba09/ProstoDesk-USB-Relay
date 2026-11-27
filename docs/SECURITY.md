# Security model

USB redirection gives the remote operator hardware-level access to the selected device. It must therefore be treated as a privileged session capability.

## Required production properties

- Explicit consent for ordinary one-time sessions.
- UI must show the exact device name plus VID:PID when available.
- The request must be bound to the authenticated ProstoDesk session and remote identity.
- No unauthenticated USB/IP listener exposed to the Internet.
- Bind/unbind operations live in a least-privileged Windows service with a narrow IPC API.
- Automatic cleanup after session loss.
- Audit events: requester, target device, time, allow/deny, attach/detach reason.
- HID devices blocked by default to prevent accidentally taking the user's keyboard or mouse.

## LAB bridge

`lab-bridge --allow-lan` is deliberately a development aid. It is raw TCP without ProstoDesk authentication or encryption. It exists only to validate real printers and service utilities before wiring the data plane into the existing ProstoDesk tunnel.
