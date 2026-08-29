# Tachyon

Tachyon is a high-performance C# (.NET 10.0) Windows WPF Desktop application (migrated from a console app) that achieves instant file searching by directly reading the NTFS Master File Table (MFT) via the USN Journal.

## Overview

The repository contains two main projects:
* `MftSearch`: A Console application for quick CLI-based interactions.
* `MftSearchWpf`: The primary Windows WPF Desktop application, providing a rich, responsive user interface.

## Architecture & Performance

Tachyon is designed with extreme performance in mind:
* **MFT Parsing Engine**: Highly optimized unmanaged memory parsing engine using Parallel processing, pre-allocated collections, and `ReadOnlySpan<byte>`/`ReadOnlySpan<char>` to minimize Garbage Collection (GC) pressure.
* **UI Responsiveness**: The WPF application follows an MVVM architecture and utilizes `VirtualizingStackPanel` for UI virtualization alongside asynchronous filtering. This prevents UI thread freezing even when handling massive datasets. Massive datasets are loaded into a background collection, and only a limited subset of matches are pushed to the UI Dispatcher.
* **Win32 P/Invoke**: Relies heavily on Win32 P/Invoke (`kernel32.dll`) for raw volume access.

## Requirements

* **Administrator Privileges**: Because it requires raw volume access to read the MFT, the application must be run as Administrator.
* **Windows OS**: Specifically designed for Windows environments (NTFS).
* **.NET 10.0**: Requires the .NET 10.0 SDK/Runtime.

## Security

As a defensive tool, the application requires maximum resilience when parsing raw data or unmanaged memory. It prefers skipping malformed data records over crashing and strictly validates lengths and bounds to prevent infinite loops and buffer over-reads.

Note: Because the application runs as Administrator, features that directly execute files (e.g., double-click events or 'Run' context menus) are intentionally omitted to prevent accidental malware execution with elevated privileges. Any external process calls sanitize and quote paths.

## Testing

The testing architecture uses xUnit for the testing framework and Moq for mocking. Tests are located in the `Tachyon.Tests` project.
