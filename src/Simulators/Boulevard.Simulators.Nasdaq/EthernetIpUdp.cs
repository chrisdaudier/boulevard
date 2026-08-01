using System.Buffers.Binary;

namespace Boulevard.Simulators.Nasdaq;

/// <summary>Strips Ethernet (with optional 802.1Q VLAN tag) / IPv4 / UDP headers to reach the application payload.</summary>
public static class EthernetIpUdp
{
    private const ushort VlanTagEtherType = 0x8100;
    private const ushort IPv4EtherType = 0x0800;
    private const byte UdpProtocolNumber = 17;

    public static bool TryExtractUdpPayload(ReadOnlySpan<byte> ethernetFrame, out ReadOnlySpan<byte> udpPayload)
    {
        udpPayload = default;

        if (ethernetFrame.Length < 14)
        {
            return false;
        }

        ushort etherType = BinaryPrimitives.ReadUInt16BigEndian(ethernetFrame.Slice(12, 2));
        int ethernetHeaderLength = 14;

        if (etherType == VlanTagEtherType)
        {
            if (ethernetFrame.Length < 18)
            {
                return false;
            }

            etherType = BinaryPrimitives.ReadUInt16BigEndian(ethernetFrame.Slice(16, 2));
            ethernetHeaderLength = 18;
        }

        if (etherType != IPv4EtherType || ethernetFrame.Length < ethernetHeaderLength + 20)
        {
            return false;
        }

        ReadOnlySpan<byte> ipPacket = ethernetFrame[ethernetHeaderLength..];
        int ipHeaderLength = (ipPacket[0] & 0x0F) * 4;

        if (ipPacket[9] != UdpProtocolNumber || ipPacket.Length < ipHeaderLength + 8)
        {
            return false;
        }

        ReadOnlySpan<byte> udpSegment = ipPacket[ipHeaderLength..];
        udpPayload = udpSegment[8..];
        return true;
    }
}
