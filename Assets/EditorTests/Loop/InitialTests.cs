using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

[TestFixture]
public class InitialTests
{
    [SetUp]
    public void Setup()
    {
       // Executed before each test.Works only if whole class is marked with [TestFixture]
       // Use it if all tests in fixture need the same or similar setup
    }

    [TearDown]
    public void TearDown()
    {
        // Clean-up after each test. Works only if whole class is marked with [TestFixture]
    }
    
    // A Test behaves as an ordinary method
    [Test]
    public void InitialTestsSimplePasses()
    {
        // Prefer AAA (Arrange-Act-Assert)
        
        // Use the Assert class to test conditions
        Assert.IsTrue(true);
    }

    // A [UnityTest] behaves like a coroutine in Play Mode. In Edit Mode you can use
    // `yield return null;` to skip a frame.
    [UnityTest]
    public IEnumerator InitialTestsWithEnumeratorPasses()
    {
        // Prefer AAA (Arrange-Act-Assert)
        
        // Use the Assert class to test conditions.
        // Use yield to skip a frame.
        yield return null;
        
        Assert.IsTrue(true);
    }
}
