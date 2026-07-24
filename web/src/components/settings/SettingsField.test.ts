import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import SettingsField from '@/components/settings/SettingsField.vue'
import i18n from '@/i18n'

describe('SettingsField', () => {
  it('renders an associated password label and show control', async () => {
    const wrapper = mount(SettingsField, {
      props: {
        field: { key: 'password', label: 'Password', type: 'password' },
        data: { password: 'secret' },
      },
      global: { plugins: [i18n] },
    })

    const input = wrapper.get('input')
    expect(input.attributes('type')).toBe('password')
    expect(wrapper.get('label').attributes('for')).toBe(input.attributes('id'))
    expect(input.attributes('autocomplete')).toBe('new-password')

    await wrapper.get('button').trigger('click')
    expect(wrapper.get('input').attributes('type')).toBe('text')
  })
})
