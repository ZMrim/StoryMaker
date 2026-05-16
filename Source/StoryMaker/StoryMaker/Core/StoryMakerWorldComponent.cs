using RimWorld.Planet;
using Verse;

namespace StoryMaker.Core;

// 序列化聚合入口。继承 WorldComponent，ExposeData() 在存档/读档时自动被调用。
// RimWorld 通过反射自动发现并实例化所有 WorldComponent 子类。
public class StoryMakerWorldComponent : WorldComponent
{
    public StoryMakerWorldComponent(World world) : base(world) { }

    public override void ExposeData()
    {
        base.ExposeData();
        StoryMakerExpose.ExposeAll();
    }
}
