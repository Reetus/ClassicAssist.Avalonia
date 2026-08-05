using System;
using System.Collections.ObjectModel;
using System.Linq;
using ClassicAssist.Data;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Misc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Additional assemblies contribute constraints through a static
    ///     <c>Initialize( ObservableCollection&lt;PropertyEntry&gt; )</c>. Rather than build a fixture DLL,
    ///     these point the assembly list at the test assembly itself - <see cref="FakePlugin" /> below is
    ///     the plugin. Assembly.LoadFrom on an already-loaded file hands back the same instance, so the
    ///     hook is discovered and invoked exactly as it would be for a real one.
    /// </summary>
    [TestClass]
    public class PluginConstraintTests
    {
        private static string[] _originalAssemblies;

        [TestInitialize]
        public void Initialize()
        {
            _originalAssemblies = AssistantOptions.Assemblies;
            FakePlugin.InitializeCount = 0;
            FakePlugin.ParameterlessCount = 0;
        }

        [TestCleanup]
        public void Cleanup()
        {
            AssistantOptions.Assemblies = _originalAssemblies;
        }

        [TestMethod]
        public void LoadPluginPropertiesInvokesConstraintHook()
        {
            AssistantOptions.Assemblies = new[] { typeof( FakePlugin ).Assembly.Location };

            ObservableCollection<PropertyEntry> constraints = new ObservableCollection<PropertyEntry>();

            AutolootPropertyRegistration.LoadPluginProperties( constraints );

            Assert.AreEqual( 1, FakePlugin.InitializeCount );
            Assert.IsTrue( constraints.Any( c => c.Name == FakePlugin.CONSTRAINT_NAME ),
                "plugin constraint should be registered" );
        }

        [TestMethod]
        public void LoadPluginPropertiesDoesNothingWithoutConfiguredAssemblies()
        {
            AssistantOptions.Assemblies = null;

            ObservableCollection<PropertyEntry> constraints = new ObservableCollection<PropertyEntry>();

            AutolootPropertyRegistration.LoadPluginProperties( constraints );

            Assert.AreEqual( 0, FakePlugin.InitializeCount );
            Assert.AreEqual( 0, constraints.Count );
        }

        /// <summary>
        ///     The parameterless hook is the one AssistantOptions.Load fires; it must not be confused with
        ///     the constraint overload, or a plugin's general setup would re-run on every filter rebuild.
        /// </summary>
        [TestMethod]
        public void InitializeOverloadsAreMatchedBySignature()
        {
            AssistantOptions.Assemblies = new[] { typeof( FakePlugin ).Assembly.Location };

            AutolootPropertyRegistration.LoadPluginProperties( new ObservableCollection<PropertyEntry>() );

            Assert.AreEqual( 1, FakePlugin.InitializeCount );
            Assert.AreEqual( 0, FakePlugin.ParameterlessCount, "parameterless hook should not fire" );

            PluginAssemblies.InvokeInitialize( Type.EmptyTypes, null );

            Assert.AreEqual( 1, FakePlugin.ParameterlessCount );
            Assert.AreEqual( 1, FakePlugin.InitializeCount, "constraint hook should not fire" );
        }

        [TestMethod]
        public void UnloadableAssemblyIsSkipped()
        {
            AssistantOptions.Assemblies = new[]
            {
                "/nonexistent/does-not-exist.dll", typeof( FakePlugin ).Assembly.Location
            };

            ObservableCollection<PropertyEntry> constraints = new ObservableCollection<PropertyEntry>();

            // A plugin built against a different version shouldn't take the constraint list down with it.
            AutolootPropertyRegistration.LoadPluginProperties( constraints );

            Assert.IsTrue( constraints.Any( c => c.Name == FakePlugin.CONSTRAINT_NAME ),
                "a bad path must not stop later assemblies loading" );
        }
    }

    /// <summary>
    ///     Stands in for a third-party plugin assembly. Top-level on purpose: the loader only considers
    ///     types where <c>Type.IsPublic</c> holds, which is false for nested types however visible they
    ///     are, so a nested fixture would be skipped for reasons that have nothing to do with the hook.
    /// </summary>
    public static class FakePlugin
    {
        public const string CONSTRAINT_NAME = "Fake Plugin Constraint";

        public static int InitializeCount { get; set; }
        public static int ParameterlessCount { get; set; }

        public static void Initialize()
        {
            ParameterlessCount++;
        }

        public static void Initialize( ObservableCollection<PropertyEntry> constraints )
        {
            InitializeCount++;

            constraints.Add( new PropertyEntry
            {
                Name = CONSTRAINT_NAME, ConstraintType = PropertyType.Predicate, Predicate = ( item, entry ) => true
            } );
        }
    }
}
