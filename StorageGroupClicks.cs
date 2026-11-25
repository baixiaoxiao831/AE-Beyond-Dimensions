using System.Diagnostics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.UI;

namespace AEBeyondDimensions;

internal sealed class StorageGroupClicks
{
    private record Click(int Index,Item Source,int Count,Item Held,int HeldCount,KeyboardState Keys,long Timestamp);
    private Click? pending;
    public void Cancel() => pending=null;

    public bool Register(TerminalStorage storage,int index,long now)
    {
        Tick(storage,now);
        var item=storage.Slots[index];
        if (pending?.Index==index && ReferenceEquals(item,pending.Source) && item.stack==pending.Count &&
            ReferenceEquals(Main.mouseItem,pending.Held) && Main.mouseItem.stack==pending.HeldCount) {
            pending=null;
            return true;
        }
        Commit(storage);
        pending=new(index,item,item.stack,Main.mouseItem,Main.mouseItem.stack,Main.keyState,now);
        return false;
    }

    public void Tick(TerminalStorage storage,long now)
    {
        // Match Terraria.UI.UserInterface's 500ms double-click interval.
        if (pending != null && Stopwatch.GetElapsedTime(pending.Timestamp,now).TotalMilliseconds>=500)
            Commit(storage);
    }

    public void Commit(TerminalStorage storage)
    {
        var click=pending;
        pending=null;
        if (click==null || !ReferenceEquals(storage.Slots[click.Index],click.Source) || click.Source.stack!=click.Count ||
            !ReferenceEquals(Main.mouseItem,click.Held) || Main.mouseItem.stack!=click.HeldCount) return;
        // Replay the reserved plain click using its original modifiers; a subsequent Shift
        // or Ctrl press is a different input and must not transfer or discard this item.
        bool left=Main.mouseLeft, release=Main.mouseLeftRelease, forced=ItemSlot.ShiftForcedOn;
        int cursor=Main.cursorOverride;
        var keys=Main.keyState;
        try {
            Main.mouseLeft=Main.mouseLeftRelease=true;
            Main.cursorOverride=-1;
            Main.keyState=click.Keys;
            ItemSlot.ShiftForcedOn=false;
            ItemSlot.LeftClick(storage.Slots,ItemSlot.Context.ChestItem,click.Index);
            storage.ReconcileDirectClick(click.Index,click.Count);
        } finally {
            Main.mouseLeft=left; Main.mouseLeftRelease=release; Main.cursorOverride=cursor;
            Main.keyState=keys; ItemSlot.ShiftForcedOn=forced;
        }
    }
}
