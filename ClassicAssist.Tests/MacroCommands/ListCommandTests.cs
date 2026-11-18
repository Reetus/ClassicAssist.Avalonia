using System;
using Xunit;
using static ClassicAssist.Data.Macros.Commands.ListCommands;

namespace ClassicAssist.Tests.MacroCommands
{
    public class ListCommandTests : IDisposable
    {
        public ListCommandTests()
        {
            CreateList( "shmoo" );
            PushList( "shmoo", 0 );
            PushList( "shmoo", 1 );
        }

        [Fact]
        public void ListExistsWillReturnTrue()
        {
            Assert.True( ListExists( "shmoo" ) );
        }

        [Fact]
        public void GetListWillReturnArray()
        {
            Assert.True( GetList( "shmoo" ) is object[] _ );
        }

        [Fact]
        public void ListCountWillReturnCorrect()
        {
            Assert.Equal( 2, List( "shmoo" ) );
        }

        public void Dispose()
        {
            RemoveList( "shmoo" );
        }
    }
}