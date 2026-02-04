# Unit Testing

Automated tests for FishNet EOS Native.

## Overview

The project includes two types of tests:

| Type | Location | Purpose |
|------|----------|---------|
| Edit Mode | `Assets/Tests/Editor/` | Test pure C# logic without Play mode |
| Play Mode | `Assets/Tests/Runtime/` | Test MonoBehaviours and async code |

## Running Tests

1. Open **Window > General > Test Runner**
2. Select **Edit Mode** or **Play Mode** tab
3. Click **Run All** or select specific tests

### Keyboard Shortcuts

- **Ctrl+Shift+T** - Open Test Runner
- **Ctrl+Shift+R** - Run all tests

## Test Structure

```
Assets/Tests/
├── Editor/
│   ├── FishNet.Transport.EOSNative.Editor.Tests.asmdef
│   └── LobbyOptionsTests.cs
└── Runtime/
    ├── FishNet.Transport.EOSNative.Runtime.Tests.asmdef
    └── TransportTests.cs
```

## Writing Edit Mode Tests

For testing pure C# logic (no MonoBehaviour):

```csharp
using NUnit.Framework;
using FishNet.Transport.EOSNative;

[TestFixture]
public class MyTests
{
    [Test]
    public void MyMethod_DoesExpectedThing()
    {
        // Arrange
        var options = new LobbyOptions();

        // Act
        options.WithGameMode("ranked");

        // Assert
        Assert.AreEqual("ranked", options.GameMode);
    }

    [Test]
    public void AnotherTest_WithMultipleAsserts()
    {
        var data = new PingData();

        Assert.IsNotNull(data.PingId);
        Assert.AreEqual(PingVisibility.Team, data.Visibility);
        Assert.AreEqual(5f, data.Duration);
    }
}
```

## Writing Play Mode Tests

For testing MonoBehaviours and async code:

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using FishNet.Transport.EOSNative;

[TestFixture]
public class MyPlayModeTests
{
    private GameObject _testObject;
    private MyComponent _component;

    [SetUp]
    public void SetUp()
    {
        _testObject = new GameObject("Test");
        _component = _testObject.AddComponent<MyComponent>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.Destroy(_testObject);
    }

    [UnityTest]
    public IEnumerator MyComponent_InitializesCorrectly()
    {
        yield return null; // Wait one frame

        Assert.IsNotNull(MyComponent.Instance);
        Assert.IsTrue(_component.IsReady);
    }

    [UnityTest]
    public IEnumerator MyComponent_AsyncOperation()
    {
        yield return null;

        _component.StartOperation();

        // Wait for operation
        yield return new WaitForSeconds(0.5f);

        Assert.IsTrue(_component.OperationComplete);
    }
}
```

## Common Assertions

```csharp
// Equality
Assert.AreEqual(expected, actual);
Assert.AreNotEqual(unexpected, actual);

// Boolean
Assert.IsTrue(condition);
Assert.IsFalse(condition);

// Null checks
Assert.IsNull(obj);
Assert.IsNotNull(obj);

// Collections
Assert.Contains(item, collection);
Assert.IsEmpty(collection);
Assert.AreEqual(3, collection.Count);

// Exceptions
Assert.Throws<ArgumentException>(() => MethodThatThrows());
Assert.DoesNotThrow(() => MethodThatWorks());

// Pass/Fail
Assert.Pass("Test passed manually");
Assert.Fail("Test failed manually");
```

## Test Categories

Group tests with categories:

```csharp
[TestFixture]
[Category("Lobbies")]
public class LobbyTests
{
    [Test]
    [Category("FastTests")]
    public void QuickTest() { }

    [Test]
    [Category("SlowTests")]
    public void SlowTest() { }
}
```

Run specific categories in Test Runner by filtering.

## Setup and Teardown

```csharp
[TestFixture]
public class MyTests
{
    // Run once before all tests in fixture
    [OneTimeSetUp]
    public void OneTimeSetUp() { }

    // Run once after all tests in fixture
    [OneTimeTearDown]
    public void OneTimeTearDown() { }

    // Run before each test
    [SetUp]
    public void SetUp() { }

    // Run after each test
    [TearDown]
    public void TearDown() { }
}
```

## Parameterized Tests

```csharp
[TestFixture]
public class ParameterizedTests
{
    [TestCase("KBM", InputType.KeyboardMouse)]
    [TestCase("CTL", InputType.Controller)]
    [TestCase("TCH", InputType.Touch)]
    public void GetInputTypeId_ReturnsCorrectId(string expected, InputType type)
    {
        Assert.AreEqual(expected, EOSInputHelper.GetInputTypeId(type));
    }

    [TestCase(1, 2, 3)]
    [TestCase(0, 0, 0)]
    [TestCase(-1, 1, 0)]
    public void Add_ReturnsSum(int a, int b, int expected)
    {
        Assert.AreEqual(expected, a + b);
    }
}
```

## Existing Test Coverage

### Edit Mode Tests

| Class | Tests |
|-------|-------|
| LobbyOptionsTests | Fluent API, defaults |
| LobbySearchOptionsTests | Filters, limits |
| InputHelperTests | Type IDs, names, fairness |
| AchievementDataTests | Data structures |
| PingSystemTests | Icons, names, data |
| ReputationTests | Levels, icons |
| HighlightTests | IDs, data storage |
| BackfillTests | Phase names, JIP messages |
| RematchTests | State names, vote tracking |

### Play Mode Tests

| Class | Tests |
|-------|-------|
| TransportTests | Offline mode, shutdown |
| ToastManagerTests | All toast types, clear |
| PingManagerTests | Settings, colors |
| AchievementsTests | Settings, ready state |
| AutoReconnectTests | Settings, session data |
| ReconnectSessionDataTests | Token generation, custom data |

## Adding New Tests

1. Create test file in appropriate folder:
   - `Assets/Tests/Editor/` for Edit Mode
   - `Assets/Tests/Runtime/` for Play Mode

2. Add `using` statements:
   ```csharp
   using NUnit.Framework;
   using FishNet.Transport.EOSNative;
   // For Play Mode:
   using UnityEngine.TestTools;
   using System.Collections;
   ```

3. Create test class with `[TestFixture]` attribute

4. Add test methods with `[Test]` or `[UnityTest]` attributes

## Best Practices

1. **One assert per test** - Makes failures clear
2. **Descriptive names** - `MethodName_Condition_ExpectedResult`
3. **Clean up** - Use TearDown to destroy objects
4. **Fast tests** - Edit Mode tests should be instant
5. **Isolated** - Tests shouldn't depend on each other
6. **No external dependencies** - Mock EOS calls when needed

## Continuous Integration

Tests can run in CI pipelines:

```bash
# Command line test runner
Unity.exe -batchmode -runTests -projectPath . -testResults results.xml
```

## Troubleshooting

**Tests not appearing:**
- Check assembly definition references
- Ensure `UNITY_INCLUDE_TESTS` define constraint
- Reimport test folder

**Play Mode tests fail:**
- Check SetUp/TearDown destroy objects
- Wait frames with `yield return null`
- Don't rely on singleton initialization order

**Edit Mode tests fail:**
- No MonoBehaviour in Edit Mode tests
- Use static methods or pure C# classes
