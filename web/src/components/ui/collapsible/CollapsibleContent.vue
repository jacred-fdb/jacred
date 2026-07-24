<script setup lang="ts">
import type { CollapsibleContentProps } from 'reka-ui'
import { CollapsibleContent } from 'reka-ui'

const props = defineProps<CollapsibleContentProps>()
</script>

<template>
  <CollapsibleContent
    data-slot="collapsible-content"
    v-bind="props"
    class="overflow-hidden data-[state=open]:animate-collapsible-down data-[state=closed]:animate-collapsible-up"
  >
    <slot />
  </CollapsibleContent>
</template>
