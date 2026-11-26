# Third-party notices

ProstoDesk USB Relay is designed to interoperate with the following independent projects. Their binaries/source are not vendored in this repository at this stage.

## usbipd-win

- Project: `dorssel/usbipd-win`
- Role: exports a physical USB device from the remote Windows PC using the USB/IP protocol.
- License: GNU GPL v3 (upstream project license).
- Integration strategy: external service/CLI (`usbipd state`, `bind`, `unbind`) and standard USB/IP TCP stream. We do not copy upstream source into the ProstoDesk assemblies.

When ProstoDesk distributes a usbipd-win binary, the corresponding source for the exact distributed version and the GPL license must be made available as required by its license.

## usbip-win2

- Project: `vadimgrn/usbip-win2`
- Role: Windows USB/IP client / virtual USB host on the controller PC.
- License: BSD 2-Clause (upstream project license).
- Integration strategy: external `usbip.exe` and signed driver. ProstoDesk keeps the driver unmodified and points it to a loopback relay port with `--tcp-port`.

## No license replacement

This notice is informational and does not replace the license texts shipped by the upstream projects. Release packaging must include the exact upstream notices/licenses corresponding to the pinned versions.
