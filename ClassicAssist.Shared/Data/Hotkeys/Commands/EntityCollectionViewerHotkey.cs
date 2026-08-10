using ClassicAssist.Shared;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Objects;
using UOC = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.Data.Hotkeys.Commands;

[HotkeyCommand( Name = "Grid Container Viewer" )]
public class EntityCollectionViewerHotkey : HotkeyCommand
{
    public override void Execute()
    {
        int serial = UOC.GetTargetSerialAsync( Strings.Target_container___ ).Result;

        if ( serial <= 0 )
        {
            return;
        }

        Entity entity = Engine.Items.GetItem( serial ) ?? (Entity) Engine.Mobiles.GetMobile( serial );

        if ( entity == null )
        {
            UOC.SystemMessage( Strings.Cannot_find_item___ );

            return;
        }

        ItemCollection collection;

        switch ( entity )
        {
            case Item item:

                // Targeting a container the client has never opened leaves us with nothing to show, so
                // ask the server for its contents first.
                if ( item.Container == null )
                {
                    UOC.WaitForContainerContentsUse( item.Serial, 1000 );
                }

                collection = item.Container ?? new ItemCollection( item.Serial );

                break;
            case Mobile mobile:
                collection = new ItemCollection( entity.Serial ) { mobile.GetEquippedItems() };

                break;
            default:
                collection = new ItemCollection( entity.Serial );

                break;
        }

        // The invoker marshals onto the UI thread itself, so this stays on the hotkey thread.
        Engine.UIInvoker?.Invoke( "EntityCollectionViewer", null, typeof( EntityCollectionViewerViewModel ),
            [collection] );
    }
}
