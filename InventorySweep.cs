using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;

namespace AEBeyondDimensions;

internal sealed class InventorySweep
{
    private bool held,active;
    private ulong visited;
    private Vector2 from,to;
    private Rectangle viewport;

    internal void Reset(){held=active=false;visited=0;}
    internal void Frame(Rectangle bounds,bool enabled){
        if(!enabled || PlayerInput.IgnoreMouseInterface || !Main.mouseLeft || !ItemSlot.ShiftInUse ||
           ItemSlot.ControlInUse || !Main.mouseItem.IsAir){Reset();return;}
        viewport=bounds;
        from=held?to:Main.MouseScreen;
        to=Main.MouseScreen;
        if(!held)active=bounds.Contains(to.ToPoint());
        held=true;
    }
    internal bool Visit(Player player,int index,Vector2 position,float size,bool specialized=false){
        if(!active)return false;
        var cell=Rectangle.Intersect(viewport,new Rectangle((int)position.X,(int)position.Y,(int)size,(int)size));
        if(cell.Width<=0 || cell.Height<=0)return false;
        if(!cell.Contains(to.ToPoint()) && !Collision.CheckAABBvLineCollision(cell.TopLeft(),cell.Size(),from,to))return false;
        ulong mask=1UL<<index;
        if((visited & mask)==0){
            visited|=mask;
            if(specialized)return false;
            var item=player.inventory[index];
            if(!item.IsAir && !item.favorited){
                if(TeamClient.Open)TeamClient.DepositSlot(index);
                else player.GetModPlayer<TerminalStorage>().Deposit(index);
            }
        }
        if(cell.Contains(to.ToPoint())){player.mouseInterface=true;Main.mouseLeftRelease=false;}
        return true;
    }
}
