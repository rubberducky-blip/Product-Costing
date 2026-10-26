import { useEffect, useState } from 'react'
import { supabase } from './supabase'

const tabId = Math.random().toString(36).slice(2)

/** Other signed-in users (or tabs) currently viewing the same record, via Supabase Realtime presence. */
export function usePresence(topic: string | null, email: string): string[] {
  const [others, setOthers] = useState<string[]>([])

  useEffect(() => {
    if (!topic) return
    const channel = supabase.channel(`presence:${topic}`, { config: { presence: { key: tabId } } })
    channel
      .on('presence', { event: 'sync' }, () => {
        const state = channel.presenceState<{ email: string }>()
        const emails = Object.entries(state)
          .filter(([key]) => key !== tabId)
          .flatMap(([, metas]) => metas.map((m) => m.email))
        setOthers([...new Set(emails)])
      })
      .subscribe((status) => {
        if (status === 'SUBSCRIBED') channel.track({ email })
      })
    return () => {
      setOthers([])
      supabase.removeChannel(channel)
    }
  }, [topic, email])

  return others
}
