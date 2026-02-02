# Contributing to FishNet-EOS-Native

Thank you for your interest in contributing! This document provides guidelines and information for contributors.

## Getting Started

1. Fork the repository
2. Clone your fork locally
3. Open the project in Unity 2022.3+ or Unity 6
4. Read through CLAUDE.md for architecture understanding
5. Check TODO.md for available tasks

## Development Setup

### Requirements
- Unity 2022.3 LTS or Unity 6
- FishNet (import from Asset Store or GitHub)
- EOS Developer Account (free at dev.epicgames.com)

### EOS SDK Setup
The EOS C# SDK is not included in the repository. To set up:

1. Download EOS C# SDK from Epic Developer Portal
2. Copy SDK folder to `Assets/Plugins/EOS/`
3. Configure DLL platform settings (see CLAUDE.md)

### Testing
- Use [ParrelSync](https://github.com/VeriorPies/ParrelSync) for multi-client testing in Editor
- Our EOSManager handles clone-unique device IDs automatically (appends project path)
- Test on actual devices for platform-specific features
- Create two separate EOS applications for isolated testing if needed

## Code Style

### Naming Conventions
- Classes: `PascalCase`
- Methods: `PascalCase`
- Private fields: `_camelCase` with underscore prefix
- Public properties: `PascalCase`
- Constants: `UPPER_SNAKE_CASE` or `PascalCase`
- Namespaces: `FishNet.Transport.EOSNative`

### Patterns
- **Callbacks over coroutines** - EOS SDK uses callbacks natively
- **async/await** - Use `TaskCompletionSource<T>` when needed
- **Result checking** - Always check `Result != Result.Success`
- **Logging** - Use FishNet's logging system

### Example
```csharp
namespace FishNet.Transport.EOSNative
{
    public class ExampleClass
    {
        private readonly Dictionary<int, string> _connections;
        private int _nextId = 1;

        public int ConnectionCount => _connections.Count;

        public async Task<Result> DoSomethingAsync()
        {
            var tcs = new TaskCompletionSource<Result>();

            var options = new SomeOptions { /* ... */ };
            _interface.DoSomething(ref options, null,
                (ref SomeCallbackInfo info) => tcs.SetResult(info.ResultCode));

            var result = await tcs.Task;

            if (result != Result.Success)
            {
                Debug.LogError($"DoSomething failed: {result}");
            }

            return result;
        }
    }
}
```

## Pull Request Process

1. **Create a branch** from `main` with a descriptive name
   - `feature/lobby-search`
   - `fix/android-init`
   - `docs/readme-update`

2. **Make your changes**
   - Follow code style guidelines
   - Add comments for complex logic
   - Update documentation if needed

3. **Test thoroughly**
   - Test in Unity Editor
   - Test builds if platform-specific
   - Test with multiple clients if networking changes

4. **Update documentation**
   - Update CHANGELOG.md with your changes
   - Update TODO.md if completing tasks
   - Update CLASSES.md if adding new classes

5. **Submit PR**
   - Clear description of changes
   - Reference any related issues
   - Include testing notes

## Issue Reporting

### Bug Reports
Include:
- Unity version
- FishNet version
- EOS SDK version
- Platform (Windows/Android/etc.)
- Steps to reproduce
- Expected vs actual behavior
- Error logs/screenshots

### Feature Requests
Include:
- Use case description
- Proposed solution (if any)
- Alternatives considered

## Areas Needing Help

Check TODO.md for current tasks. Priority areas:

1. **Platform Testing** - Android, iOS, macOS, Linux
2. **Documentation** - Examples, tutorials, API docs
3. **Edge Cases** - Error handling, disconnection scenarios
4. **Performance** - Optimization, profiling

## Questions?

- Open a GitHub Issue for questions
- Check existing issues first
- Join FishNet Discord for community support

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
