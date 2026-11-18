using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using ClassicAssist.Shared;
using ClassicAssist.Tests;
using ClassicAssist.UI.ViewModels;
using Xunit.v3;

[assembly: StaticEventLeakGuard]

namespace ClassicAssist.Tests
{
    /// <summary>
    ///     Fails any test that leaves one of its own handlers attached to a static event in the product
    ///     assemblies (Engine.InternalPacketSentEvent and friends), and detaches it.
    ///     <para>
    ///         The suite shares one process, so a handler that outlives its test - typically because an
    ///         assert threw before the matching <c>-=</c> - goes on to assert against, or swallow, the
    ///         packets of every later test. Those later tests then fail with messages that have nothing to
    ///         do with them, depending on run order. This moves the failure to the test that caused it.
    ///     </para>
    ///     <para>
    ///         Only handlers whose code lives in this test assembly are considered, so product code that
    ///         subscribes for good (a singleton wiring itself to Engine.ConnectedEvent on first use) is not
    ///         reported. Handlers attached in a test class constructor are in the "before" snapshot and are
    ///         ignored too; a class that subscribes in its constructor should unsubscribe in Dispose.
    ///     </para>
    /// </summary>
    [AttributeUsage( AttributeTargets.Assembly )]
    public sealed class StaticEventLeakGuardAttribute : BeforeAfterTestAttribute
    {
        private static readonly Lazy<FieldInfo[]> _eventFields = new Lazy<FieldInfo[]>( FindStaticEventFields );
        private static readonly Assembly _testAssembly = typeof( StaticEventLeakGuardAttribute ).Assembly;

        // Plain statics: the assembly runs one test at a time (TestAssemblyInfo.cs), and an async test can
        // reach After on a different thread from Before.
        private static Dictionary<FieldInfo, Delegate[]> _before;

        // View model handlers seen at the end of the previous test, detached when the next one starts.
        private static List<(FieldInfo Field, Delegate Handler)> _viewModelHandlers = new List<(FieldInfo, Delegate)>();

        public override void Before( MethodInfo methodUnderTest, IXunitTest test )
        {
            DetachPreviousViewModels();

            _before = _eventFields.Value.ToDictionary( f => f, Handlers );
        }

        public override void After( MethodInfo methodUnderTest, IXunitTest test )
        {
            Dictionary<FieldInfo, Delegate[]> before = _before;
            _before = null;

            RememberViewModels();

            if ( before == null )
            {
                return;
            }

            StringBuilder leaks = new StringBuilder();

            foreach ( FieldInfo field in _eventFields.Value )
            {
                List<Delegate> remaining = before[field].ToList();

                foreach ( Delegate handler in Handlers( field ) )
                {
                    // Match one-for-one so a handler subscribed twice is counted twice.
                    int index = remaining.FindIndex( d => d.Equals( handler ) );

                    if ( index >= 0 )
                    {
                        remaining.RemoveAt( index );
                        continue;
                    }

                    if ( !IsTestCode( handler ) )
                    {
                        continue;
                    }

                    field.SetValue( null, Delegate.Remove( (Delegate) field.GetValue( null ), handler ) );

                    leaks.AppendLine(
                        $"  {field.DeclaringType?.Name}.{field.Name} <- {handler.Method.DeclaringType?.Name}.{handler.Method.Name}" );
                }
            }

            if ( leaks.Length > 0 )
            {
                throw new InvalidOperationException(
                    "Test left handlers attached to static events (now detached). Unsubscribe in a finally block or Dispose:" +
                    Environment.NewLine + leaks );
            }
        }

        /// <summary>
        ///     View models subscribe to static packet events in their constructors and, outside the app's
        ///     own lifetime, are never torn down - SkillsTabViewModel, for one, keeps handling every later
        ///     test's 0x3A and throws when no skill data is loaded. No view model is a singleton, so one that
        ///     outlives the test that made it is always a leak. Its handlers are noted when a test ends
        ///     (Dispose may still use the view model) and detached as the next test starts; a view model
        ///     the next test's constructor creates has new delegates and is left alone.
        /// </summary>
        private static void RememberViewModels()
        {
            _viewModelHandlers = _eventFields.Value
                .SelectMany( f => Handlers( f ).Where( h => h.Target is BaseViewModel ).Select( h => ( f, h ) ) )
                .ToList();
        }

        private static void DetachPreviousViewModels()
        {
            foreach ( (FieldInfo field, Delegate handler) in _viewModelHandlers )
            {
                field.SetValue( null, Delegate.Remove( (Delegate) field.GetValue( null ), handler ) );
            }

            _viewModelHandlers = new List<(FieldInfo, Delegate)>();
        }

        private static bool IsTestCode( Delegate handler )
        {
            // Lambdas and local functions compile into closure types nested in the test class, so the
            // declaring type's assembly identifies test code whether or not there is a target.
            return handler.Method.DeclaringType?.Assembly == _testAssembly;
        }

        private static Delegate[] Handlers( FieldInfo field )
        {
            return ( field.GetValue( null ) as Delegate )?.GetInvocationList() ?? Array.Empty<Delegate>();
        }

        private static FieldInfo[] FindStaticEventFields()
        {
            Assembly[] productAssemblies = { typeof( Engine ).Assembly };

            List<FieldInfo> fields = new List<FieldInfo>();

            foreach ( Assembly assembly in productAssemblies )
            {
                Type[] types;

                try
                {
                    types = assembly.GetTypes();
                }
                catch ( ReflectionTypeLoadException e )
                {
                    types = e.Types.Where( t => t != null ).ToArray();
                }

                foreach ( Type type in types )
                {
                    if ( type.ContainsGenericParameters )
                    {
                        continue;
                    }

                    foreach ( EventInfo eventInfo in type.GetEvents( BindingFlags.Static | BindingFlags.Public |
                                                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly ) )
                    {
                        // Field-like events store their handlers in a private field of the same name.
                        FieldInfo field = type.GetField( eventInfo.Name,
                            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly );

                        if ( field != null && typeof( Delegate ).IsAssignableFrom( field.FieldType ) )
                        {
                            fields.Add( field );
                        }
                    }
                }
            }

            return fields.ToArray();
        }
    }
}
