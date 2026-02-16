# Contributing to AIVory Monitor .NET Agent

Thank you for your interest in contributing to the AIVory Monitor .NET Agent. Contributions of all kinds are welcome -- bug reports, feature requests, documentation improvements, and code changes.

## How to Contribute

- **Bug reports**: Open an issue at [GitHub Issues](https://github.com/aivorynet/agent-dotnet/issues) with a clear description, steps to reproduce, and your environment details (.NET version, OS, runtime).
- **Feature requests**: Open an issue describing the use case and proposed behavior.
- **Pull requests**: See the Pull Request Process below.

## Development Setup

### Prerequisites

- .NET 6.0 SDK or later

### Build and Test

```bash
cd monitor-agents/agent-dotnet
dotnet build
dotnet test
```

### Running the Agent

Add the NuGet package to your project and call `AddAIVoryMonitor()` in your startup configuration. See the README for integration details.

## Coding Standards

- Follow the existing code style in the repository.
- Write tests for all new features and bug fixes.
- Follow the [.NET coding conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions).
- Use `DiagnosticSource` APIs correctly and avoid blocking the host application.
- Ensure compatibility with .NET 6.0+.

## Pull Request Process

1. Fork the repository and create a feature branch from `main`.
2. Make your changes and write tests.
3. Ensure all tests pass (`dotnet test`) and the build succeeds (`dotnet build`).
4. Submit a pull request on [GitHub](https://github.com/aivorynet/agent-dotnet) or GitLab.
5. All pull requests require at least one review before merge.

## Reporting Bugs

Use [GitHub Issues](https://github.com/aivorynet/agent-dotnet/issues). Include:

- .NET version (`dotnet --version`) and OS
- Agent version
- Exception details or error output
- Minimal reproduction steps

## Security

Do not open public issues for security vulnerabilities. Report them to **security@aivory.net**. See [SECURITY.md](SECURITY.md) for details.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
