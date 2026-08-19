using System.Text;

namespace Boulevard.Protocol.Fix.Tests;

/// <summary>
/// Builds known-good raw FIX byte messages for tests, independent of the production
/// FixMessageWriter under test - takes a body (tags 35 onward, '|' standing in for SOH so the
/// literal is readable in source) and computes real BodyLength/CheckSum around it by hand.
/// </summary>
internal static class FixTestFixtures
{
    public static byte[] Build(string beginString, string body)
    {
        string normalizedBody = body.Replace('|', '\x01');
        if (!normalizedBody.EndsWith('\x01'))
        {
            normalizedBody += '\x01';
        }

        string beginField = $"8={beginString}\x01";
        // FixMessageWriter always reserves a fixed 6-digit zero-padded BodyLength slot (so it can
        // backpatch in place without shifting later bytes) - match that convention here too, so
        // round-trip tests compare our own canonical output against itself, not against an
        // arbitrarily-differently-formatted fixture.
        string bodyLengthField = $"9={normalizedBody.Length:D6}\x01";
        string withoutChecksum = beginField + bodyLengthField + normalizedBody;

        byte[] withoutChecksumBytes = Encoding.ASCII.GetBytes(withoutChecksum);
        byte sum = 0;
        foreach (byte b in withoutChecksumBytes)
        {
            sum += b;
        }

        string checksumField = $"10={sum:D3}\x01";
        return Encoding.ASCII.GetBytes(withoutChecksum + checksumField);
    }
}
