import { useCallback, useEffect, useState } from "react";
import type { Channel } from "@finos/fdc3";
import { getFdc3Agent } from "./fdc3";

export function useFdc3Channels() {
  const [connected, setConnected] = useState(false);
  const [channels, setChannels] = useState<Channel[]>([]);
  const [currentChannelId, setCurrentChannelId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    getFdc3Agent()
      .then(async (fdc3) => {
        if (cancelled) {
          return;
        }

        setConnected(true);
        const userChannels = await fdc3.getUserChannels();
        if (cancelled) {
          return;
        }

        setChannels(userChannels);
        const current = await fdc3.getCurrentChannel();
        if (!cancelled) {
          setCurrentChannelId(current?.id ?? null);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setConnected(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const selectChannel = useCallback(
    async (channelId: string) => {
      const fdc3 = await getFdc3Agent();

      if (currentChannelId === channelId) {
        await fdc3.leaveCurrentChannel();
        setCurrentChannelId(null);
      } else {
        await fdc3.joinUserChannel(channelId);
        setCurrentChannelId(channelId);
      }
    },
    [currentChannelId],
  );

  return { connected, channels, currentChannelId, selectChannel };
}
