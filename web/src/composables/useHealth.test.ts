import { defineComponent } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useHealth } from '@/composables/useHealth'
import { apiClient } from '@/lib/api/client'

const Host = defineComponent({
  setup: useHealth,
  template: '<div>{{ isLoading }}:{{ isOnline }}</div>',
})

function render() {
  return mount(Host, {
    global: {
      plugins: [
        [
          VueQueryPlugin,
          {
            queryClient: new QueryClient({
              defaultOptions: { queries: { retry: false } },
            }),
          },
        ],
      ],
    },
  })
}

afterEach(() => vi.restoreAllMocks())

describe('useHealth', () => {
  it('stays offline for a non-OK health payload', async () => {
    vi.spyOn(apiClient, 'getHealth').mockResolvedValue({ status: 'DEGRADED' })
    const wrapper = render()
    await flushPromises()
    expect(wrapper.text()).toContain('false:false')
  })

  it('reports online only for OK', async () => {
    vi.spyOn(apiClient, 'getHealth').mockResolvedValue({ status: 'OK' })
    const wrapper = render()
    await flushPromises()
    expect(wrapper.text()).toContain('false:true')
  })
})
