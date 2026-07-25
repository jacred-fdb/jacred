import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import TorrentActionRail from '@/components/search/TorrentActionRail.vue'
import i18n from '@/i18n'

const passthrough = {
  template: '<div><slot /></div>',
  props: ['asChild'],
}

function render(hasMagnet: boolean) {
  return mount(TorrentActionRail, {
    props: {
      magnet: hasMagnet ? 'magnet:?xt=urn:btih:abc' : '',
      hasMagnet,
      torState: 'idle',
    },
    global: {
      plugins: [i18n],
      stubs: {
        Tooltip: passthrough,
        TooltipTrigger: passthrough,
        TooltipContent: passthrough,
      },
    },
  })
}

describe('TorrentActionRail', () => {
  it('disables every action when no safe magnet is available', () => {
    const wrapper = render(false)
    const buttons = wrapper.findAll('button')
    expect(buttons).toHaveLength(4)
    expect(
      buttons.every((button) => button.attributes('disabled') !== undefined),
    ).toBe(true)
    expect(wrapper.find('a').exists()).toBe(false)
  })

  it('uses the magnet URL only when enabled', () => {
    const wrapper = render(true)
    expect(wrapper.get('a').attributes('href')).toBe(
      'magnet:?xt=urn:btih:abc',
    )
  })
})
