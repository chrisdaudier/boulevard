import type { Channel } from "@finos/fdc3";

interface ChannelSelectorProps {
  channels: Channel[];
  currentChannelId: string | null;
  onSelect: (channelId: string) => void;
}

/** Standard FDC3 UX convention: a row of colored dots, one per user channel, click to join/leave. */
export function ChannelSelector({ channels, currentChannelId, onSelect }: ChannelSelectorProps) {
  if (channels.length === 0) {
    return null;
  }

  return (
    <div style={{ display: "flex", gap: 6, alignItems: "center" }}>
      {channels.map((channel) => (
        <button
          key={channel.id}
          onClick={() => onSelect(channel.id)}
          title={channel.displayMetadata?.name ?? channel.id}
          style={{
            width: 18,
            height: 18,
            borderRadius: "50%",
            background: channel.displayMetadata?.color ?? "#888",
            border: currentChannelId === channel.id ? "2px solid white" : "2px solid transparent",
            cursor: "pointer",
            padding: 0,
          }}
        />
      ))}
    </div>
  );
}
