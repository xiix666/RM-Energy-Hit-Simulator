using System;
using System.Linq;
using NUnit.Framework;

namespace Simulation.EnergyHit.Tests
{
    /// <summary>
    /// Covers the <c>internal sealed EnergyHitStatisticsDebuggerPage</c> declared in
    /// <c>Assets/Scripts/Simulation/EnergyHitDebuggerWindow.cs</c>.
    /// OnDraw is deliberately not invoked: GUILayout calls are only valid during an
    /// OnGUI pass, so the test would be non-deterministic outside one.
    /// </summary>
    [TestFixture]
    public sealed class EnergyHitStatisticsDebuggerPageTests
    {
        private const string PageTypeName = "Simulation.EnergyHit.EnergyHitStatisticsDebuggerPage";
        private const string DebuggerWindowInterfaceName = "GameFramework.Debugger.IDebuggerWindow";

        private Type _pageType;

        [SetUp]
        public void SetUp()
        {
            _pageType = EnergyHitTestReflection.GetType(PageTypeName);
        }

        private object CreatePage()
        {
            return Activator.CreateInstance(_pageType, true);
        }

        [Test]
        public void PageType_IsInternalSealed()
        {
            Assert.IsTrue(_pageType.IsNotPublic, "class must stay internal");
            Assert.IsTrue(_pageType.IsSealed, "class must stay sealed");
        }

        [Test]
        public void PageType_ImplementsIDebuggerWindow()
        {
            bool implements = _pageType.GetInterfaces()
                .Any(i => i.FullName == DebuggerWindowInterfaceName);

            Assert.IsTrue(implements, $"{_pageType.FullName} must implement {DebuggerWindowInterfaceName}");
        }

        [Test]
        public void PageType_CanBeConstructedByTheDebugger()
        {
            object page = CreatePage();

            Assert.IsNotNull(page);
            Assert.IsInstanceOf(_pageType, page);
        }

        [Test]
        public void Initialize_WithoutArguments_DoesNotThrow()
        {
            object page = CreatePage();
            MethodInfoHolder initialize = new MethodInfoHolder(_pageType, "Initialize");

            Assert.DoesNotThrow(() => initialize.Invoke(page, new object[] { new object[0] }));
        }

        [Test]
        public void Initialize_WithArbitraryArguments_DoesNotThrow()
        {
            object page = CreatePage();
            MethodInfoHolder initialize = new MethodInfoHolder(_pageType, "Initialize");

            Assert.DoesNotThrow(() => initialize.Invoke(page,
                new object[] { new object[] { 1, "config", null, 3.5f } }));
        }

        [Test]
        public void LifecycleMethods_AreNoOpsAndDoNotThrow()
        {
            object page = CreatePage();

            Assert.DoesNotThrow(() => new MethodInfoHolder(_pageType, "OnEnter").Invoke(page));
            Assert.DoesNotThrow(() => new MethodInfoHolder(_pageType, "OnUpdate")
                .Invoke(page, 0.016f, 0.016f));
            Assert.DoesNotThrow(() => new MethodInfoHolder(_pageType, "OnLeave").Invoke(page));
            Assert.DoesNotThrow(() => new MethodInfoHolder(_pageType, "Shutdown").Invoke(page));
        }

        [Test]
        public void OnUpdate_WithExtremeElapsedValues_DoesNotThrow()
        {
            object page = CreatePage();
            MethodInfoHolder onUpdate = new MethodInfoHolder(_pageType, "OnUpdate");

            Assert.DoesNotThrow(() => onUpdate.Invoke(page, 0f, 0f));
            Assert.DoesNotThrow(() => onUpdate.Invoke(page, -1f, -1f));
            Assert.DoesNotThrow(() => onUpdate.Invoke(page, float.MaxValue, float.MaxValue));
        }

        [Test]
        public void LifecycleMethods_CanBeCalledRepeatedly()
        {
            object page = CreatePage();
            MethodInfoHolder onEnter = new MethodInfoHolder(_pageType, "OnEnter");
            MethodInfoHolder onLeave = new MethodInfoHolder(_pageType, "OnLeave");

            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    onEnter.Invoke(page);
                    onLeave.Invoke(page);
                }
            });
        }

        [Test]
        public void Shutdown_ThenLifecycleMethods_StillDoNotThrow()
        {
            object page = CreatePage();
            new MethodInfoHolder(_pageType, "Shutdown").Invoke(page);

            Assert.DoesNotThrow(() => new MethodInfoHolder(_pageType, "OnEnter").Invoke(page));
            Assert.DoesNotThrow(() => new MethodInfoHolder(_pageType, "OnLeave").Invoke(page));
        }

        [Test]
        public void OnDraw_IsPublicParameterless()
        {
            MethodInfoHolder onDraw = new MethodInfoHolder(_pageType, "OnDraw");

            Assert.IsTrue(onDraw.Method.IsPublic);
            Assert.AreEqual(0, onDraw.Method.GetParameters().Length);
        }

        [Test]
        public void DrawItem_IsStaticPrivateHelperWithTitleAndContent()
        {
            System.Reflection.MethodInfo drawItem = _pageType.GetMethod("DrawItem",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);

            Assert.IsNotNull(drawItem);
            Assert.IsTrue(drawItem.IsPrivate);
            Assert.AreEqual(2, drawItem.GetParameters().Length);
            Assert.AreEqual(typeof(string), drawItem.GetParameters()[0].ParameterType);
            Assert.AreEqual(typeof(string), drawItem.GetParameters()[1].ParameterType);
        }

        /// <summary>Small facade so tests can invoke reflected methods without reflection boilerplate.</summary>
        private readonly struct MethodInfoHolder
        {
            public MethodInfoHolder(Type type, string name)
            {
                Method = type.GetMethod(name) ??
                         throw new MissingMethodException(type.FullName, name);
            }

            public System.Reflection.MethodInfo Method { get; }

            public void Invoke(object target, params object[] args)
            {
                Method.Invoke(target, args.Length == 0 ? null : args);
            }
        }
    }
}
