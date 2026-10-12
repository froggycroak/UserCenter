<template>
  <canvas ref="canvasEl" class="header-map-line-art" aria-hidden="true" />
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'

const canvasEl = ref<HTMLCanvasElement | null>(null)

let ro: ResizeObserver | null = null

const SEED_VAL = 0x6d617031

type Rect = { x: number; y: number; w: number; h: number }

function mixSeed(a: number, b: number) {
  return Math.imul(a ^ b, 0x9e3779b1) >>> 0
}

function hash(a: number, b: number) {
  let h = mixSeed(SEED_VAL, a)
  h = mixSeed(h, b)
  h ^= h >>> 16
  h = Math.imul(h, 0x7feb352d)
  h ^= h >>> 15
  return (h >>> 0) / 0xffffffff
}

function riverDistance(x: number, y: number, w: number, h: number) {
  const x1 = -w * 0.08
  const y1 = h * 1.08
  const x2 = w * 1.08
  const y2 = -h * 0.08
  const dx = x2 - x1
  const dy = y2 - y1
  const len = Math.hypot(dx, dy) || 1
  return Math.abs((x - x1) * dy - (y - y1) * dx) / len
}

function riverHalfWidth(w: number, h: number) {
  return Math.min(w, h) * (0.09 + 0.02 * Math.sin(0.7))
}

function sideOfRiver(x: number, y: number, w: number, h: number) {
  const x1 = -w * 0.08
  const y1 = h * 1.08
  const x2 = w * 1.08
  const y2 = -h * 0.08
  const cross = (x - x1) * (y2 - y1) - (y - y1) * (x2 - x1)
  return cross > 0 ? 1 : -1
}

function readStroke(host: HTMLElement) {
  const style = getComputedStyle(host)
  const line = style.getPropertyValue('--header-map-stroke').trim()
  const w = parseFloat(style.getPropertyValue('--header-map-stroke-w'))
  return {
    stroke: line || 'rgba(0, 0, 0, 0.16)',
    lineWidth: Number.isFinite(w) ? w : 0.55
  }
}

function strokeParcel(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  bw: number,
  bh: number,
  style: { stroke: string; lineWidth: number }
) {
  if (bw < 6 || bh < 6) return
  const px = Math.round(x) + 0.5
  const py = Math.round(y) + 0.5
  const pw = Math.max(1, Math.round(bw) - 1)
  const ph = Math.max(1, Math.round(bh) - 1)
  ctx.strokeStyle = style.stroke
  ctx.lineWidth = style.lineWidth
  ctx.lineJoin = 'miter'
  ctx.strokeRect(px, py, pw, ph)
}

function drawMicroFootprints(
  ctx: CanvasRenderingContext2D,
  block: Rect,
  row: number,
  col: number,
  style: { stroke: string; lineWidth: number }
) {
  const pad = 3
  const innerW = block.w - pad * 2
  const innerH = block.h - pad * 2
  if (innerW < 10 || innerH < 8) return

  const count = 1 + Math.floor(hash(row, col + 900) * 3.2)
  ctx.strokeStyle = style.stroke
  ctx.lineWidth = Math.max(0.55, style.lineWidth * 0.85)

  for (let i = 0; i < count; i++) {
    const fw = 4 + hash(row, col + i * 3) * (innerW * 0.35)
    const fh = 3 + hash(row, col + i * 3 + 1) * (innerH * 0.35)
    const fx = block.x + pad + hash(row, col + i * 5) * Math.max(0, innerW - fw)
    const fy = block.y + pad + hash(row, col + i * 5 + 2) * Math.max(0, innerH - fh)
    ctx.strokeRect(Math.round(fx) + 0.5, Math.round(fy) + 0.5, Math.round(fw), Math.round(fh))
  }
}

function drawRotatedParcels(
  ctx: CanvasRenderingContext2D,
  w: number,
  h: number,
  style: { stroke: string; lineWidth: number }
) {
  const STREET = 7
  const angles = [-0.32, 0.18]

  for (const angle of angles) {
    ctx.save()
    ctx.translate(w * 0.38, h * 0.55)
    ctx.rotate(angle)
    ctx.translate(-w * 0.38, -h * 0.55)

    const rw = w * 2.2
    const rh = h * 2.4
    const ox = -w * 0.55
    const oy = -h * 0.7
    const halfRiver = riverHalfWidth(w, h)

    let row = 0
    let y = oy
    while (y < oy + rh) {
      const rowH = 22 + hash(row, angle * 100) * 18
      let col = 0
      let x = ox + (row % 2 === 1 ? rowH * 0.55 : 0)
      while (x < ox + rw) {
        const colW = 26 + hash(row, col + 1) * 22
        const bw = colW - STREET
        const bh = rowH - STREET
        const cx = x + bw * 0.5
        const cy = y + bh * 0.5

        const dist = riverDistance(cx, cy, w, h)
        const onDenseSide = sideOfRiver(cx, cy, w, h) < 0 || cx < w * 0.52
        const skipRiver = dist < halfRiver
        const sparseSkip = !onDenseSide && hash(row, col + 77) > 0.42

        if (!skipRiver && !sparseSkip && bw >= 8 && bh >= 8) {
          const block = { x, y, w: bw, h: bh }
          strokeParcel(ctx, x, y, bw, bh, style)
          if (bw > 18 && bh > 14 && hash(row, col + 50) > 0.25) {
            drawMicroFootprints(ctx, block, row, col, style)
          }
        }

        x += colW
        col++
      }
      y += rowH
      row++
    }

    ctx.restore()
  }
}

function drawRiverBanks(
  ctx: CanvasRenderingContext2D,
  w: number,
  h: number,
  style: { stroke: string; lineWidth: number }
) {
  const x1 = -w * 0.08
  const y1 = h * 1.08
  const x2 = w * 1.08
  const y2 = -h * 0.08
  const dx = x2 - x1
  const dy = y2 - y1
  const len = Math.hypot(dx, dy) || 1
  const nx = (-dy / len)
  const ny = (dx / len)
  const half = riverHalfWidth(w, h)

  ctx.strokeStyle = style.stroke
  ctx.lineWidth = style.lineWidth
  ctx.setLineDash([])

  for (const sign of [-1, 1]) {
    ctx.beginPath()
    const steps = 24
    for (let i = 0; i <= steps; i++) {
      const t = i / steps
      const bx = x1 + dx * t
      const by = y1 + dy * t
      const wobble = Math.sin(t * 11 + sign) * 2.5 + hash(Math.floor(t * 20), sign + 3) * 2
      const px = bx + nx * (half * sign + wobble * sign)
      const py = by + ny * (half * sign + wobble * sign)
      if (i === 0) ctx.moveTo(px, py)
      else ctx.lineTo(px, py)
    }
    ctx.stroke()
  }
}

function drawDashedContours(
  ctx: CanvasRenderingContext2D,
  w: number,
  h: number,
  style: { stroke: string; lineWidth: number }
) {
  ctx.save()
  ctx.translate(w * 0.72, h * 0.4)
  ctx.rotate(0.12)
  ctx.translate(-w * 0.72, -h * 0.4)

  ctx.strokeStyle = style.stroke
  ctx.lineWidth = Math.max(0.55, style.lineWidth * 0.9)
  ctx.setLineDash([5, 6])

  for (let i = 0; i < 9; i++) {
    const y = h * 0.05 + i * (h * 0.11) + (hash(i, 40) - 0.5) * 8
    ctx.beginPath()
    const x0 = w * 0.48 + hash(i, 41) * 20
    const x1 = w * 1.15
    ctx.moveTo(x0, y)
    for (let s = 1; s <= 5; s++) {
      const t = s / 5
      const x = x0 + (x1 - x0) * t
      const yy = y + Math.sin(t * 6 + i) * 3
      ctx.lineTo(x, yy)
    }
    ctx.stroke()
  }

  ctx.setLineDash([])
  ctx.restore()
}

function drawSparseFootprints(
  ctx: CanvasRenderingContext2D,
  w: number,
  h: number,
  style: { stroke: string; lineWidth: number }
) {
  ctx.strokeStyle = style.stroke
  ctx.lineWidth = style.lineWidth
  let i = 0
  while (i < 28) {
    const x = w * 0.58 + hash(i, 601) * w * 0.42
    const y = hash(i, 602) * h * 0.95
    if (riverDistance(x, y, w, h) < riverHalfWidth(w, h) * 1.1) {
      i++
      continue
    }
    const bw = 8 + hash(i, 603) * 18
    const bh = 6 + hash(i, 604) * 14
    strokeParcel(ctx, x, y, bw, bh, style)
    i++
  }
}

function drawMapLineArt(
  ctx: CanvasRenderingContext2D,
  w: number,
  h: number,
  host: HTMLElement
) {
  ctx.clearRect(0, 0, w, h)
  ctx.save()
  ctx.beginPath()
  ctx.rect(0, 0, w, h)
  ctx.clip()

  const style = readStroke(host)
  drawRotatedParcels(ctx, w, h, style)
  drawRiverBanks(ctx, w, h, style)
  drawDashedContours(ctx, w, h, style)
  drawSparseFootprints(ctx, w, h, style)

  ctx.restore()
}

onMounted(() => {
  const canvas = canvasEl.value
  const host = canvas?.parentElement
  if (!canvas || !host) return

  const ctx = canvas.getContext('2d', { alpha: true })
  if (!ctx) return

  const dpr = Math.min(window.devicePixelRatio || 1, 2)

  const resize = () => {
    requestAnimationFrame(() => {
      const rect = host.getBoundingClientRect()
      const w = Math.max(1, Math.round(rect.width))
      const h = Math.max(1, Math.round(rect.height))
      canvas.width = Math.round(w * dpr)
      canvas.height = Math.round(h * dpr)
      canvas.style.width = `${w}px`
      canvas.style.height = `${h}px`
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
      drawMapLineArt(ctx, w, h, host)
    })
  }

  resize()
  ro = new ResizeObserver(resize)
  ro.observe(host)
  const inner = host.querySelector('.header-inner')
  if (inner) ro.observe(inner)
})

onUnmounted(() => {
  ro?.disconnect()
})
</script>
