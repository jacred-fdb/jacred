<script setup lang="ts">
import { cn } from '@/lib/utils'

defineProps<{
  listView: boolean
  isSmUp: boolean
}>()
</script>

<template>
  <div
    aria-hidden="true"
    :data-layout="listView ? 'list' : 'card'"
    :class="
      cn(
        'animate-pulse border border-border/60 jr-elevated',
        listView
          ? 'flex flex-col gap-1 rounded-md px-2 py-1.5 sm:flex-row sm:items-center sm:gap-2 sm:py-1'
          : 'jr-result-card flex flex-col gap-0 overflow-hidden rounded-lg sm:gap-1.5 sm:p-2',
      )
    "
  >
    <template v-if="listView">
      <div class="flex min-w-0 flex-1 items-center gap-2">
        <div class="size-8 shrink-0 rounded-md bg-muted-foreground/20" />
        <div
          :class="
            cn(
              'min-w-0 flex-1 rounded-md bg-muted-foreground/20',
              isSmUp ? 'h-4' : 'h-8',
            )
          "
        />
      </div>
      <div
        :class="
          cn(
            'flex items-center gap-1.5',
            isSmUp ? 'shrink-0' : 'flex-wrap',
          )
        "
      >
        <div class="h-5 w-12 rounded-md bg-muted-foreground/15" />
        <div class="h-5 w-16 rounded-md bg-muted-foreground/15" />
        <div class="h-5 w-10 rounded-md bg-muted-foreground/15" />
        <div class="h-5 w-10 rounded-md bg-muted-foreground/15" />
        <div class="h-5 w-20 rounded-md bg-muted-foreground/15" />
      </div>
      <div
        :class="
          cn(
            'flex gap-1 rounded-md border border-border/40 bg-muted/30 p-0.5',
            isSmUp ? 'shrink-0' : 'w-full',
          )
        "
      >
        <div
          v-for="i in 4"
          :key="i"
          class="size-8 rounded-md bg-muted-foreground/15"
        />
      </div>
    </template>

    <template v-else>
      <div
        class="flex items-start gap-2 px-2.5 pt-2 pb-1.5 sm:p-0"
      >
        <div class="size-8 shrink-0 rounded-md bg-muted-foreground/20" />
        <div class="min-w-0 flex-1 space-y-1.5">
          <div class="h-4 w-[92%] rounded-md bg-muted-foreground/20" />
          <div
            v-if="!isSmUp"
            class="h-3.5 w-[70%] rounded-md bg-muted-foreground/15"
          />
        </div>
        <div class="mt-0.5 h-5 w-12 shrink-0 rounded-md bg-muted-foreground/15" />
      </div>
      <div
        class="flex flex-wrap items-center gap-x-2 gap-y-1 px-2.5 py-1.5 sm:p-0"
      >
        <div class="h-5 w-16 rounded-md bg-muted-foreground/15" />
        <div class="h-5 w-10 rounded-md bg-muted-foreground/15" />
        <div class="h-5 w-10 rounded-md bg-muted-foreground/15" />
        <div class="h-5 w-20 rounded-md bg-muted-foreground/15" />
      </div>
      <div class="px-2.5 pt-1.5 pb-2 sm:p-0">
        <div
          class="flex gap-1 rounded-md border border-border/40 bg-muted/30 p-0.5 sm:inline-flex"
        >
          <div
            v-for="i in 4"
            :key="i"
            class="size-8 rounded-md bg-muted-foreground/15 sm:size-9"
          />
        </div>
      </div>
    </template>
  </div>
</template>
