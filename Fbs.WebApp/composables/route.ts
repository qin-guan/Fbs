/* eslint-disable @typescript-eslint/no-explicit-any */
import type { MaybeRefOrGetter, Ref, MaybeRef } from 'vue'
import type { Router, RouteParamValueRaw } from 'vue-router'

import { tryOnScopeDispose } from '@vueuse/shared'
import { customRef, nextTick, toValue, watch } from 'vue'

export type RouteQueryValueRaw = RouteParamValueRaw | string[]

export type RouteHashValueRaw = string | null | undefined

export interface ReactiveRouteOptions {
  /**
   * Mode to update the router query, ref is also acceptable
   *
   * @default 'replace'
   */
  mode?: MaybeRef<'replace' | 'push'>

  /**
   * Route instance, use `useRoute()` if not given
   */
  route?: ReturnType<typeof useRoute>

  /**
   * Router instance, use `useRouter()` if not given
   */
  router?: ReturnType<typeof useRouter>
}

export interface ReactiveRouteOptionsWithTransform<V, R> extends ReactiveRouteOptions {
  /**
   * Function to transform data before return, or an object with one or both functions:
   * `get` to transform data before returning, and `set` to transform data before setting
   */
  transform?:
  | ((val: V) => R)
  | ({
    get?: (value: V) => R
    set?: (value: R) => V
  })
}

const _queue = new WeakMap<Router, Map<string, any>>()

export function useRouteQuery(
  name: string
): Ref<undefined | null | string | string[]>

export function useRouteQuery<
  T extends RouteQueryValueRaw = RouteQueryValueRaw,
  K = T,
>(
  name: string,
  defaultValue?: MaybeRefOrGetter<T>,
  options?: ReactiveRouteOptionsWithTransform<T, K>
): Ref<K>

export function useRouteQuery<
  T extends RouteQueryValueRaw = RouteQueryValueRaw,
  K = T,
>(
  name: string,
  defaultValue?: MaybeRefOrGetter<T>,
  options: ReactiveRouteOptionsWithTransform<T, K> = {},
): Ref<K> {
  const {
    mode = 'replace',
    route = useRoute(),
    router = useRouter(),
    transform,
  } = options

  let transformGet = (value: T) => value as any as K
  let transformSet = (value: K) => value as any as T

  if (typeof transform === 'function') {
    transformGet = transform
  }
  else if (transform) {
    if (transform.get)
      transformGet = transform.get
    if (transform.set)
      transformSet = transform.set
  }

  if (!_queue.has(router))
    _queue.set(router, new Map())

  const _queriesQueue = _queue.get(router)!

  let query = route.query[name] as any

  tryOnScopeDispose(() => {
    query = undefined
  })

  let _trigger: () => void

  const proxy = customRef<any>((track, trigger) => {
    _trigger = trigger

    return {
      get() {
        track()

        return transformGet(query !== undefined ? query : toValue(defaultValue))
      },
      set(v) {
        v = transformSet(v)

        if (query === v)
          return

        query = (v === toValue(defaultValue)) ? undefined : v
        _queriesQueue.set(name, (v === toValue(defaultValue)) ? undefined : v)

        trigger()

        nextTick(() => {
          if (_queriesQueue.size === 0)
            return

          const newQueries = Object.fromEntries(_queriesQueue.entries())
          _queriesQueue.clear()

          const { params, query, hash } = route

          router[toValue(mode)]({
            params,
            query: { ...query, ...newQueries },
            hash,
          })
        })
      },
    }
  })

  watch(
    () => route.query[name],
    (v) => {
      if (query === transformGet(v as T))
        return

      query = v

      _trigger()
    },
    { flush: 'sync' },
  )

  return proxy as any as Ref<K>
}
