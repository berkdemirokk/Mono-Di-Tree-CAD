namespace FittedResearch;

// Fixed source-design dart relations over independently generated segment IDs.
// These are source correspondence candidates, not physical sewing approval.
internal static class PartialDartRelations
{
    internal sealed record Rule(string Snapshot,string Owner,int SourceRule,int EdgeA,int EdgeB,bool FlipA,bool FlipB);
    internal static IReadOnlyList<Rule> Create(string id)
    {
        var geometry=PartialFittedEngine.Create(id); var keys=geometry.Segments.Select(e=>(e.Panel,e.Edge)).ToHashSet();
        var rules=new List<Rule>();
        void Add(string panel,int index,int a,int b)
        {
            if(!keys.Contains((panel,a))||!keys.Contains((panel,b))) throw new InvalidOperationException("Dart source edge missing");
            rules.Add(new(id,panel,index,a,b,false,false));
        }
        Add("left_ftorso",0,5,6);Add("left_ftorso",1,1,2);
        Add("right_ftorso",0,6,5);Add("right_ftorso",1,10,9);
        for(int i=0;i<4;i++) { int first=new[]{3,6,10,13}[i];Add("skirt_back",i,first,first+1); }
        Add("skirt_front",0,9,8);Add("skirt_front",1,5,4);
        return rules.AsReadOnly();
    }
}
