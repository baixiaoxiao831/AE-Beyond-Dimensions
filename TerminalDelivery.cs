using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AEBeyondDimensions;

// The cursor and ImproveGame's reward list belong to the local character.
// Let vanilla create and synchronize the actual item before the server routes it.
public sealed class TerminalDelivery : GlobalItem
{
    public override bool InstancePerEntity => true;
    private byte owner=255,team;
    private bool matching;
    internal bool Pending => owner!=255;

    internal static void Drop(Player player,Item item,bool matchesOnly)
    {
        var copy=item.Clone();
        var route=copy.GetGlobalItem<TerminalDelivery>();
        route.owner=(byte)player.whoAmI;
        route.team=(byte)player.team;
        route.matching=matchesOnly;
        player.QuickSpawnItem(player.GetSource_Misc("AE terminal transfer"),copy,copy.stack);
    }

    public override void NetSend(Item item,BinaryWriter writer)
    {
        writer.Write(owner);
        if(Pending){writer.Write(team);writer.Write(matching);}
    }
    public override void NetReceive(Item item,BinaryReader reader)
    {
        owner=reader.ReadByte();
        if(Pending){team=reader.ReadByte();matching=reader.ReadBoolean();}
    }
    public override bool CanPickup(Item item,Player player)=>!Pending;
    public override bool CanStackInWorld(Item destination,Item source)=>
        !destination.GetGlobalItem<TerminalDelivery>().Pending && !source.GetGlobalItem<TerminalDelivery>().Pending;
    public override void PostUpdate(Item item)
    {
        if(!Pending || Main.netMode!=NetmodeID.Server)return;
        var player=Main.player[owner];
        int index=item.whoAmI;
        owner=255;
        if(player.DistanceSQ(item.Center)<160*160 && TeamNetwork.TryBank(player,team,out var key,out var bank) &&
           (!matching || !item.IsACoin && !item.favorited && bank.Stock.Exists(i=>i.type==item.type))){
            bank.Store(item.Clone());
            item.TurnToAir();item.active=false;
            TeamNetwork.SupplyChanged(key,bank);
        }
        NetMessage.SendData(MessageID.SyncItem,-1,-1,null,index);
    }
}
