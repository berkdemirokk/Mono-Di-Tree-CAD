namespace FittedResearch;

// Partial source-formula reconstruction, GarmentCode d449629, MIT,
// Copyright (c) 2024 Maria Korosteleva. Sentinels only: no real input API.
internal static class PartialFittedEngine
{
    internal readonly record struct P(double X,double Y);
    internal sealed record Segment(string Panel,int Edge,P Start,P End,string SourceKind);
    internal sealed record Result(IReadOnlyList<Segment> Segments)
    {
        public string Status=>"unresolved";
        public bool ProductionAllowed=>false;
        public bool ExportAllowed=>false;
    }
    internal static Result Create(string syntheticId)
    {
        // Immutable, built-in synthetic records. Other measurements and designs
        // are intentionally unavailable; no user-supplied values are accepted.
        var b=syntheticId switch {
            "synthetic-base"=>new Body(100,48,80,36,104,55,37,41,26,172,23,17,21),
            "synthetic-distinct"=>new Body(96,43,74,34,102,54,39,42,26,172,23,17,21),
            _=>throw new ArgumentException("Unknown synthetic case",nameof(syntheticId))
        };
        double bustLine=2*b.VerticalBustLine/3+b.BustLine/3;
        double frontWidth=(b.Bust-b.BackWidth)/2,frontWaist=(b.Waist-b.WaistBack)/2;
        double dartWidth=(frontWidth-frontWaist)*2/3, dartPoint=b.BustPoints/2,sideY=b.WaistLine-bustLine;
        double sideX=-(frontWaist+dartWidth),sideStart=-frontWidth;
        var result=new List<Segment>();
        void Add(string panel,int index,double x1,double y1,double x2,double y2,string kind="Edge") => result.Add(new(panel,index,new(x1,y1),new(x2,y2),kind));
        Add("right_ftorso",7,sideStart,sideY,sideX,0);
        Add("right_ftorso",8,sideX,0,-dartPoint-dartWidth,0);
        var frontCut=DartCut.Horizontal(-dartPoint-dartWidth,-dartPoint,0,dartWidth,.9*sideY,true);
        Add("right_ftorso",9,frontCut.Start.X,frontCut.Start.Y,frontCut.Tip.X,frontCut.Tip.Y);
        Add("right_ftorso",10,frontCut.Tip.X,frontCut.Tip.Y,frontCut.End.X,frontCut.End.Y);
        Add("left_ftorso",1,-frontCut.End.X,frontCut.End.Y,-frontCut.Tip.X,frontCut.Tip.Y);
        Add("left_ftorso",2,-frontCut.Tip.X,frontCut.Tip.Y,-frontCut.Start.X,frontCut.Start.Y);
        double shoulderTan=Math.Tan(22*Math.PI/180),frontFrac=frontWidth/b.Bust;
        double adjustment=shoulderTan*(frontWidth-18), maxLen=b.WaistOverBust-adjustment;
        double backAdjustment=shoulderTan*(backWidthForSide()-18),fbDifference=(2*frontFrac-.5)*b.Bust;
        double sideLength=b.WaistLine-backAdjustment-shoulderTan*fbDifference;
        double sideDartWidth=maxLen-sideLength,sideDartLeg=.75*(frontWidth-dartPoint);
        var sideCut=DartCut.Horizontal(sideY,sideY+sideDartWidth,0,sideDartWidth,sideDartLeg,true);
        Add("right_ftorso",5,-frontWidth,sideCut.End.X,-frontWidth+sideCut.Tip.Y,sideCut.Tip.X);
        Add("right_ftorso",6,-frontWidth+sideCut.Tip.Y,sideCut.Tip.X,-frontWidth,sideCut.Start.X);
        Add("left_ftorso",5,frontWidth,sideCut.Start.X,frontWidth-sideCut.Tip.Y,sideCut.Tip.X);
        Add("left_ftorso",6,frontWidth-sideCut.Tip.Y,sideCut.Tip.X,frontWidth,sideCut.End.X);
        double backWidthForSide()=>b.BackWidth/2;
        Add("right_ftorso",11,-dartPoint,0,0,0);
        Add("left_ftorso",0,0,0,dartPoint,0);
        Add("left_ftorso",3,dartPoint+dartWidth,0,-sideX,0);
        Add("left_ftorso",4,-sideX,0,-sideStart,sideY);
        double backWidth=b.BackWidth/2,backWaist=b.WaistBack/2,difference=Math.Max(backWidth,backWaist)-backWaist;
        double sideAdjustment=difference<4?0:difference/6;
        Add("right_btorso",7,-Math.Max(backWidth,backWaist)+sideAdjustment,0,-backWidth,sideY);
        Add("left_btorso",5,backWidth,sideY,Math.Max(backWidth,backWaist)-sideAdjustment,0);
        void Skirt(string panel,double hips,double extension,bool front)
        {
            // Source's pivot truncates toward zero before darts are inserted.
            double legLength=b.Height-b.HeadLength-b.WaistLine-b.HipsDepth;
            double length=.4*legLength, depth=b.HipsDepth*extension;
            double shift=Math.Tan(5*Math.PI/180)*depth;
            double waist=front?frontWaist:backWaist;
            shift=Math.Min(shift,hips-waist);
            double px=Math.Truncate(hips),py=Math.Truncate(length+depth);
            double left=-px,right=2*hips-px,bottom=-py,hip=length-py,top=length+depth-py;
            double dartWidthSkirt=hips-waist-shift;
            if(dartWidthSkirt<=0) throw new ArgumentException("Unsupported absent skirt dart");
            double centerOffset=hips-px;
            double legDepth=b.HipsDepth*(front?.8:.85)-(b.HipsDepth-depth);
            if(front)
            {
                double half=dartWidthSkirt/2,position=b.BustPoints/2;
                var plus=DartCut.Horizontal(centerOffset+position+dartWidthSkirt,centerOffset+position,top,dartWidthSkirt,legDepth,false);
                var minus=DartCut.Horizontal(centerOffset-position,centerOffset-position-dartWidthSkirt,top,dartWidthSkirt,legDepth,false);
                Add(panel,4,plus.Start.X,top,plus.Tip.X,plus.Tip.Y); Add(panel,5,plus.Tip.X,plus.Tip.Y,plus.End.X,top);
                Add(panel,8,minus.Start.X,top,minus.Tip.X,minus.Tip.Y); Add(panel,9,minus.Tip.X,minus.Tip.Y,minus.End.X,top);
            }
            else
            {
                double pos=9, dist=pos*.5, w=dartWidthSkirt/2;
                double[] centers=[centerOffset-pos-dist/2-3*dartWidthSkirt/4,centerOffset-pos+dist/2-dartWidthSkirt/4,centerOffset+pos-dist/2+dartWidthSkirt/4,centerOffset+pos+dist/2+3*dartWidthSkirt/4];
                int[] first=[3,6,10,13];
                for(int i=0;i<4;i++)
                {
                    double leg=legDepth*((i==0||i==3)?.9:1);
                    var dart=DartCut.Horizontal(centers[i]-w/2,centers[i]+w/2,top,w,leg,false);
                    Add(panel,first[i],dart.Start.X,top,dart.Tip.X,dart.Tip.Y); Add(panel,first[i]+1,dart.Tip.X,dart.Tip.Y,dart.End.X,top);
                }
            }
            if(front)
            {
                Add(panel,0,left,bottom,right,bottom); Add(panel,1,right,bottom,right,hip);
                Add(panel,2,right,hip,right-shift,top,"CurveEdgeEndpointsOnly");
                Add(panel,11,left+shift,top,left,hip,"CurveEdgeEndpointsOnly"); Add(panel,12,left,hip,left,bottom);
            }
            else
            {
                Add(panel,0,left,bottom,left,hip); Add(panel,1,left,hip,left+shift,top,"CurveEdgeEndpointsOnly");
                Add(panel,16,right-shift,top,right,hip,"CurveEdgeEndpointsOnly"); Add(panel,17,right,hip,right,bottom); Add(panel,18,right,bottom,left,bottom);
            }
        }
        Skirt("skirt_front",(b.Hips-b.HipsBack)/2,1,true);
        Skirt("skirt_back",b.HipsBack/2,1.05,false);
        return new(result.AsReadOnly());
    }
    private sealed record Body(double Bust,double BackWidth,double Waist,double WaistBack,double Hips,double HipsBack,
        double WaistLine,double WaistOverBust,double HeadLength,double Height,double HipsDepth,double BustPoints,double VerticalBustLine)
    {
        public double BustLine=>26;
    }
}
