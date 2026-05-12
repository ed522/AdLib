# AdLib

AdLib is an ad-hoc, minimal-setup filesharing solution designed for quick and secure peer-to-peer file transfers without
the need for complex infrastructure or central servers. Security is built-in and

<!-- screenshot: main screen -->

## Key capabilities

- **GUI-based:** User-friendly interface for browsing. No more guesswork with a CLI.
- **Secure by design:** All communications are encrypted using TLS, with complete control over who you trust.
- **Identity-based authentication:** Uses a secure out-of-band one-time setup method, similar to SSH. No insecure
  password-based authentication.
- **Robust Protocol:** Handles complex file operations including:
    - Recursive directory transfers that maintain structure
    - File listing and status requests
    - Integrity verification
- **Reliable Transfers:** Implements chunk-based data transfer with temporary file buffering to prevent corruption in
  case of connection loss.
- **Cross-Platform:** Built with [Avalonia UI](https://avaloniaui.net/) for a consistent experience across Windows,
  Linux, and macOS.
- **Minimal Setup:** No central server or account registration required. Host and connect with a single click. Setting
  up takes less than 5 minutes.

## Installation

*Note: The app is currently in development and not yet available for download. You can still run directly
with `dotnet`.*

### Portable Options

AdLib will be available as a portable executable that can be run without installation.

- [ ] Windows (Self-contained)
- [ ] Linux (AppImage / Binaries)
- [ ] macOS (App Bundle)

### Installed Options

Installation packages for major operating systems will be provided in the future.

### Prerequisites

- .NET 9.0 Runtime (or newer) may be required depending on the package type.

## Usage

1. **Launch AdLib:** Open the application on both the sender and receiver machines.
2. **Create/unlock identity:** Set up your digital identity. This certificate will be used to authenticate you to other
   peers. Private keys are locally stored and password-protected.
3. **Start a Server:** If you want to receive files or allow browsing, start a server. The app will only share the
   folder you specify.
4. **Connect to the server:** Enter the IP address or hostname of the remote peer on the client to initiate a session.
5. **Share files:** Once connected, you can browse, upload, or download files directly through the interface.

## Contribution Notes

We are currently **not accepting Pull Requests** for this project. If you have a suggestion, please open an issue in the
repository, however bug reports will not be accepted until the project is in a more stable state.

## License & Credits

### License

This project is licensed under the GNU GPL v3.0 (or later). See `LICENSE` for details.

This was specifically chosen because there is not any valid way to link with this as a library, thus the only works
affected by using a copyleft license are ones that are directly based on AdLib's source, and therefore should also be
open source. However, we are open to suggestions for alternative licenses if the need arises.

### Credits

AdLib is made possible by the following open-source projects:

- **[Avalonia UI](https://avaloniaui.net/):** a cross-platform UI framework for .NET, and the reason for this project's
  existence. Licensed under MIT.
- **[BouncyCastle](https://www.bouncycastle.org/csharp/):** a complete cryptographic library for .NET. Licensed under
  MIT.
- **[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet):** MVVM utilities for WPF and Avalonia. Licensed
  under MIT.
- **[.NET](https://dotnet.microsoft.com/):** the cross-platform runtime. .NET Runtime is licensed under MIT.

---
*© ed522 2026. Licensed under the MIT license. Do distribute.*
