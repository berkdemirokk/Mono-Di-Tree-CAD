using TekstilCAD.FileFormats;

try
{
    var document=ProjectStorage.Load(args[0]);
    Console.WriteLine("ACCEPT " + System.Text.Json.JsonSerializer.Serialize(document));
    return 0;
}
catch(Exception error)
{
    Console.WriteLine("REJECT " + error.GetType().Name);
    return 2;
}
