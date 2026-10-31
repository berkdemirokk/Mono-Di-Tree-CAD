using System.Globalization;
using System.Text;

namespace TekstilCAD.FileFormats;

/// <summary>Decodes only DXF Unicode escapes for an explicitly selected simple TEXT value.</summary>
internal static class DxfTextileTextDecoder
{
    public static string Decode(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        // At most 128 UTF-16 units, each represented by a seven-character escape.
        if(raw.Length==0 || raw.Length>128*7)throw new InvalidDataException("DXF TEXT değeri boş veya çok uzun.");
        var result=new StringBuilder(Math.Min(raw.Length,128));
        for(var i=0;i<raw.Length;i++)
        {
            var c=raw[i];
            if(c=='\\')
            {
                if(i+6>=raw.Length || raw[i+1]!='U' || raw[i+2]!='+' ||
                   !ushort.TryParse(raw.AsSpan(i+3,4),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out var unit))
                    throw new InvalidDataException("DXF TEXT Unicode kaçışı geçersiz veya desteklenmiyor.");
                c=(char)unit;i+=6;
            }
            result.Append(c);
            if(result.Length>128)throw new InvalidDataException("DXF TEXT değeri 128 karakter sınırını aşıyor.");
        }
        var value=result.ToString();
        if(string.IsNullOrWhiteSpace(value) || value!=value.Trim())throw new InvalidDataException("DXF TEXT değeri boş veya kenar boşluğu içeriyor.");
        for(var i=0;i<value.Length;i++)
        {
            var c=value[i];
            // A single percent is ordinary material text (for example 100%
            // cotton); paired percent signs can introduce DXF TEXT controls.
            // Check after Unicode decoding too, so escaped percent signs cannot
            // hide an ambiguous control sequence.
            if(char.IsControl(c) || c is '\u2028' or '\u2029' or '\\' ||
               (c=='%' && i+1<value.Length && value[i+1]=='%'))
                throw new InvalidDataException("DXF TEXT değeri desteklenmeyen karakter içeriyor.");
            if(char.IsHighSurrogate(c))
            {
                if(++i>=value.Length || !char.IsLowSurrogate(value[i]))
                    throw new InvalidDataException("DXF TEXT Unicode vekil çifti geçersiz.");
            }
            else if(char.IsLowSurrogate(c))throw new InvalidDataException("DXF TEXT Unicode vekil çifti geçersiz.");
        }
        return value;
    }
}
