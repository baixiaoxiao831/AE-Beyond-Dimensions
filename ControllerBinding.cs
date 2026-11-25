using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AEBeyondDimensions;

public sealed class ControllerBinding : ModTileEntity
{
    internal byte Team;
    internal static bool TryTile(int x,int y,out Point16 anchor)
    {
        anchor=default;
        if(!WorldGen.InWorld(x,y))return false;
        var tile=Main.tile[x,y];
        if(!tile.HasTile || TileLoader.GetTile(tile.TileType) is not ControllerTileBase)return false;
        anchor=new Point16(x-tile.TileFrameX/18,y-tile.TileFrameY/18);
        return true;
    }
    public override bool IsTileValidForEntity(int x,int y)=>TryTile(x,y,out var a) && a.X==x && a.Y==y;
    internal static byte ColorAt(Point16 a)=>ByPosition.TryGetValue(a,out var e) && e is ControllerBinding b?b.Team:(byte)0;
    internal static string Neutral(Point16 a)=>"controller/"+a.X+"/"+a.Y;
    internal static bool OpenFor(Point16 anchor,int team)
    {
        int color=ColorAt(anchor);
        if(color!=0)return color==team;
        if(team==0)return true;
        if(team is <1 or >5)return false;
        int id=ModContent.GetInstance<ControllerBinding>().Place(anchor.X,anchor.Y);
        if(id<0)return false;
        var binding=(ControllerBinding)ByID[id];binding.Team=(byte)team;
        string key=TeamWorld.KeyFor(team,Guid.Empty);
        if(!TeamWorld.Banks.TryGetValue(key,out var target))TeamWorld.Banks[key]=target=new();
        if(TeamWorld.Banks.Remove(Neutral(anchor),out var previous)){
            foreach(var item in previous.Stock)target.Store(item);
            foreach(var station in previous.Stations){
                if(target.Stations.Exists(i=>i.type==station.type))target.Store(station);
                else target.Stations.Add(station);
            }
            target.Orders.AddRange(previous.Orders);
            target.Paused.UnionWith(previous.Paused);
            foreach(var (order,row) in previous.OrderData)target.OrderData[order]=row;
            if(Main.netMode==NetmodeID.Server)TeamNetwork.Broadcast(key,target);
        }
        if(Main.netMode==NetmodeID.Server)NetMessage.SendData(MessageID.TileEntitySharing,-1,-1,null,id,anchor.X,anchor.Y);
        return true;
    }
    public override void NetSend(BinaryWriter writer)=>writer.Write(Team);
    public override void NetReceive(BinaryReader reader)=>Team=reader.ReadByte();
    public override void SaveData(TagCompound tag)=>tag["team"]=(int)Team;
    public override void LoadData(TagCompound tag)=>Team=(byte)Math.Clamp(tag.GetInt("team"),0,5);
}

// Vanilla places multiplayer furniture on the server from PlaceObject.
// Associate only a freshly placed controller with the actual sending player.
public sealed class ControllerPlacement : ModSystem
{
    public override void Load()=>On_MessageBuffer.GetData+=Placed;
    private static void Placed(On_MessageBuffer.orig_GetData orig,MessageBuffer buffer,int start,int length,out int type)
    {
        bool placing=Main.netMode==NetmodeID.Server && length>=7 && buffer.whoAmI<Main.maxPlayers &&
            buffer.readBuffer[start]==MessageID.PlaceObject;
        int x=0,y=0,tile=0;
        if(placing){
            x=BitConverter.ToInt16(buffer.readBuffer,start+1);y=BitConverter.ToInt16(buffer.readBuffer,start+3);
            tile=BitConverter.ToInt16(buffer.readBuffer,start+5);
            placing=tile>=0 && tile<TileLoader.TileCount && TileLoader.GetTile(tile) is ControllerTileBase &&
                WorldGen.InWorld(x,y,1) && !Main.tile[x,y].HasTile;
        }
        orig(buffer,start,length,out type);
        if(placing && Main.tile[x,y].TileType==tile && ControllerBinding.TryTile(x,y,out var a))
            ControllerBinding.OpenFor(a,Main.player[buffer.whoAmI].team);
    }
}
