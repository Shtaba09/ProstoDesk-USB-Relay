# Architecture

## Production data path

```text
Remote Windows PC                                      Operator Windows PC
-----------------                                      -------------------
Physical USB device
       |
 usbipd-win :3240 (loopback / local firewall scope)
       |
 ProstoDesk USB Host Adapter
       |
 [dedicated authenticated ProstoDesk session stream]
       |
 ProstoDesk USB Controller Adapter
       |
 127.0.0.1:<ephemeral port>
       |
 usbip-win2 virtual host controller
       |
 Windows PnP + vendor driver + service utility
```

The final ProstoDesk implementation should not expose usbipd TCP/3240 on WAN. The Host adapter connects locally to usbipd, while the Controller adapter terminates an ephemeral loopback listener. Raw USB/IP bytes are transported over a dedicated ProstoDesk logical stream.

## Control plane

Control messages remain separate from USB/IP payload bytes:

- enumerate devices
- request access
- consent / deny
- bind / force-bind decision
- start relay
- stop relay
- health / disconnect

The existing ProstoDesk session identity and authorization must be reused. A USB relay request is valid only for the active session that created it.

## Data plane

Do not reinterpret URBs in the first implementation. Forward USB/IP byte streams transparently. This dramatically reduces driver/protocol complexity and lets us reuse mature USB/IP endpoints.

QoS recommendation inside ProstoDesk:

1. session control and USB control traffic
2. audio/chat control
3. interactive desktop control
4. USB bulk traffic
5. video/desktop image bulk traffic

Exact scheduling should avoid starving either USB transactions or remote-control input.

## Disconnect semantics

A mapping must be torn down when any of these happens:

- operator clicks Disconnect
- remote user revokes permission
- ProstoDesk session ends
- USB device is unplugged
- relay transport fails beyond reconnect grace period
- controller process exits

The controller should detach only the VHCI port created by that mapping, never `detach --all`.

## Device policy

MVP default:

- printers/MFP/service adapters: allowed after explicit user consent
- serial adapters: allowed after explicit user consent
- storage: disabled until separately enabled by policy
- keyboard/mouse/HID: blocked by default
- cameras/audio/isochronous devices: out of scope for first stable release

Trusted Admin Token mode may later permit per-device or per-class unattended access, but it must be opt-in on the controlled PC.
