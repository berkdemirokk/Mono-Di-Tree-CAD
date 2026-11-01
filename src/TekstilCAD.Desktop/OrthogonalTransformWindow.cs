using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Detached orthogonal transformation with a cancellable, snapshot-guarded commit.</summary>
internal sealed class OrthogonalTransformWindow : Window
{
    private readonly MainWindow workspace;
    private readonly OrthogonalGradingTransform operation;
    private readonly string operationName;
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12) };
    private readonly Button cancel=new() { Content="İptal et",Padding=new Thickness(12,7,12,7),IsCancel=true };
    private CancellationTokenSource? cancellation;private bool closed,started;
    internal Action? BeforeCalculationForVerification { get; set; }
    internal Task? TaskForVerification { get; private set; }
    internal string StatusForVerification=>status.Text;
    internal void CancelForVerification()=>cancellation?.Cancel();
    internal Task RunForVerification()=>RunAsync();
    internal OrthogonalTransformWindow(MainWindow workspace,OrthogonalGradingTransform operation)
    {
        this.workspace=workspace;this.operation=operation;operationName=operation switch {
            OrthogonalGradingTransform.RotateLeft=>"90° sağa döndürme",OrthogonalGradingTransform.RotateRight=>"90° sola döndürme",
            OrthogonalGradingTransform.RotateHalf=>"180° döndürme",OrthogonalGradingTransform.MirrorVertical=>"Dikey eksene göre aynalama",
            OrthogonalGradingTransform.MirrorHorizontal=>"Yatay eksene göre aynalama",_=>throw new ArgumentOutOfRangeException(nameof(operation)) };
        Owner=workspace;Title=operationName+" hesaplanıyor";Width=460;SizeToContent=SizeToContent.Height;MinWidth=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var body=new StackPanel { Margin=new Thickness(20) };Content=body;
        AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);body.Children.Add(status);body.Children.Add(cancel);
        cancel.Click+=(_,_)=> { cancellation?.Cancel();SetStatus("İptal istendi; süren hesap tamamlandığında sonucu uygulanmadan bırakılacak."); };
        Closing+=(_,e)=> { if(cancellation is not null) { e.Cancel=true;cancellation.Cancel();SetStatus("İptal istendi; süren hesap tamamlandığında sonuç uygulanmayacak."); } };
        Closed+=(_,_)=> { closed=true;cancellation?.Cancel(); };
        Loaded+=(_,_)=>TaskForVerification=RunAsync();
    }
    private void SetStatus(string text)
    {
        status.Text=text;AutomationProperties.SetName(status,text);
        if(UIElementAutomationPeer.CreatePeerForElement(status) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private async Task RunAsync()
    {
        if(started || closed)return;started=true;
        var stamp=workspace.CaptureGrainlineEdit();
        if(stamp is null) { SetStatus("Proje, bekleyen çalışma veya seçim nedeniyle dönüşüm uygulanmadı.");return; }
        if(!workspace.CanRetainCurrentUndoSnapshot)
        { SetStatus("Geri alma sınırı aşılıyor; dönüşüm uygulanmadı.");return; }
        var id=stamp.PieceId;var before=stamp.Snapshot;var source=before.Pieces.SingleOrDefault(p=>p.Id==id);
        if(source is null || source.Points.Count==0) { SetStatus("Kaynak parça bulunamadı; dönüşüm uygulanmadı.");return; }
        using var cts=new CancellationTokenSource();cancellation=cts;var committed=false;
        SetStatus("Büyük parça arka planda hesaplanıyor; proje henüz değişmedi. İptal isteği hesap bitince uygulanır.");
        try
        {
            var hook=BeforeCalculationForVerification;
            var prepared=await Task.Run(()=>
            {
                hook?.Invoke();var token=cts.Token;token.ThrowIfCancellationRequested();
                if(source.Notches is not null)_=MarkedPieceTranslation.Translate(source,0,0,token);
                var center=Geometry.Bounds(source.Points).Center;
                List<Point2> Transform(IReadOnlyList<Point2> points)=>operation switch {
                    OrthogonalGradingTransform.RotateLeft=>Geometry.Rotate(points,90,center),
                    OrthogonalGradingTransform.RotateRight=>Geometry.Rotate(points,-90,center),
                    OrthogonalGradingTransform.RotateHalf=>Geometry.Rotate(points,180,center),
                    OrthogonalGradingTransform.MirrorVertical=>Geometry.Mirror(points,true,center.X),
                    OrthogonalGradingTransform.MirrorHorizontal=>Geometry.Mirror(points,false,center.Y),
                    _=>throw new ArgumentOutOfRangeException(nameof(operation)) };
                var rotated=Transform(source.Points);token.ThrowIfCancellationRequested();
                var marks=source.Notches?.Select(m=>new NotchMark { VertexIndex=m.VertexIndex,Position=rotated[m.VertexIndex] }).ToList();
                var rule=before.GradingRules.FirstOrDefault(r=>r.SourcePieceId==id);
                var rotatedRule=rule?.OrthogonalFor(source,rotated,operation);
                var grain=GrainlineTransform.Transform(source.Grainline,source.Points[0],rotated[0],Transform);
                token.ThrowIfCancellationRequested();
                var candidate=before.Clone();var target=candidate.Pieces.Single(p=>p.Id==id);target.Points=rotated;target.Notches=marks;target.Grainline=grain;
                if(rotatedRule is not null)candidate.GradingRules[candidate.GradingRules.FindIndex(r=>r.SourcePieceId==id)]=rotatedRule;
                ProjectStorage.Validate(candidate,token);token.ThrowIfCancellationRequested();
                return (candidate,stale:rule is not null && rotatedRule is null && !rotated.SequenceEqual(source.Points));
            },cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if(closed || workspace.IsWindowClosed)return;
            if(workspace.GrainlineEditProblem(stamp) is not null || !workspace.CanRetainCurrentUndoSnapshot)
            { SetStatus("Proje, seçim veya bekleyen çalışma değişti; dönüşüm uygulanmadı.");return; }
            cts.Token.ThrowIfCancellationRequested();
            workspace.CommitObserved(operationName+" uygulandı",()=>
            {
                var live=workspace.Document.Pieces.FindIndex(p=>p.Id==id);var result=prepared.candidate.Pieces.Single(p=>p.Id==id);
                workspace.Document.Pieces[live]=result;
                var mapped=prepared.candidate.GradingRules.FirstOrDefault(r=>r.SourcePieceId==id);
                var liveRule=workspace.Document.GradingRules.FindIndex(r=>r.SourcePieceId==id);
                if(mapped is not null && liveRule>=0)workspace.Document.GradingRules[liveRule]=mapped;
            },()=>committed=true);
            if(prepared.stale)workspace.ShowCanvasError("Dönüşüm uygulandı; kayıtlı serileme kuralı önceki kontura ait. Serilemeden önce kuralı gözden geçirin.");
            SetStatus(committed ? operationName+" uygulandı; tek adımda geri alınabilir." : "Dönüşümde değişiklik yok; proje ve geri alma geçmişi korundu.");
            if(IsVisible)CloseAfterCompletion();
        }
        catch(OperationCanceledException) when(cts.IsCancellationRequested && !committed)
        { if(!closed)SetStatus("Dönüşüm iptal edildi; proje değiştirilmedi."); }
        catch(Exception ex) when(ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { if(committed)workspace.RequireMutationReview();if(!closed)SetStatus(committed ? "Dönüşüm uygulandı ancak görünüm yenilenemedi. Tekrar denemeden önce sonucu ve geri alma geçmişini kontrol edin: "+ex.Message : "Dönüşüm uygulanmadı: "+ex.Message); }
        finally { if(ReferenceEquals(cancellation,cts))cancellation=null; }
    }
    private void CloseAfterCompletion()
    {
        cancellation=null;Close();
    }
}
