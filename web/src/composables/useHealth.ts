import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import { apiClient } from '@/lib/api/client'

/** Polls `/health` and exposes a boolean online signal for the shell status pill. */
export function useHealth() {
  const query = useQuery({
    queryKey: ['health'],
    queryFn: async () => {
      const data = await apiClient.getHealth()
      return data
    },
    refetchInterval: 30_000,
    retry: 1,
  })

  const isOnline = computed(() => {
    if (query.isError.value || !query.isSuccess.value) return false
    const data = query.data.value
    const status =
      typeof data === 'string'
        ? data
        : data && typeof data === 'object'
          ? data.status
          : undefined
    return typeof status === 'string' && status.toUpperCase() === 'OK'
  })

  return {
    ...query,
    isOnline,
  }
}
