# Changelog

All notable changes to the AIVory Monitor .NET Agent will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-02-16

### Added
- Automatic exception capture via AppDomain and DiagnosticSource
- ASP.NET Core middleware for HTTP request context
- Manual exception capture with CaptureException and CaptureAndRethrow
- Extension method `exception.CaptureWithAIVory()`
- Singleton agent pattern with thread-safe initialization
- WebSocket transport to AIVory backend
- ProcessExit handler for graceful shutdown
- ILogger integration for structured logging
- .NET 6, 7, and 8 target framework support
- Environment variable and programmatic configuration
