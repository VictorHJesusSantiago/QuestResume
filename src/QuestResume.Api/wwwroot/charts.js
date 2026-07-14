// Minimal dependency-free canvas chart renderer for the dashboard (no CDN/npm build — the app is
// offline-first). Provides a donut chart (used for "Documentos por tipo") and a bar/line chart
// (used for "Perguntas ao longo do tempo"). Colors follow the app's CSS custom properties so the
// charts adapt to the light/dark theme.

const CHART_PALETTE = ['#38bdf8', '#4ade80', '#facc15', '#f87171', '#a78bfa', '#fb923c', '#2dd4bf', '#f472b6', '#818cf8', '#94a3b8'];

function cssVar(name, fallback) {
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return value || fallback;
}

/**
 * Renders a donut chart of `entries` (array of [label, value]) into the given <canvas>.
 * Also appends a text legend after the canvas inside `container`.
 */
function renderDonutChart(container, entries) {
  container.replaceChildren();

  if (!entries.length) {
    container.append('Nenhum dado disponível ainda.');
    return;
  }

  const canvas = document.createElement('canvas');
  const size = 220;
  const dpr = window.devicePixelRatio || 1;
  canvas.width = size * dpr;
  canvas.height = size * dpr;
  canvas.style.width = `${size}px`;
  canvas.style.height = `${size}px`;
  canvas.className = 'chart-canvas';
  container.appendChild(canvas);

  const ctx = canvas.getContext('2d');
  ctx.scale(dpr, dpr);

  const total = entries.reduce((sum, [, value]) => sum + value, 0);
  const cx = size / 2;
  const cy = size / 2;
  const outerRadius = size / 2 - 6;
  const innerRadius = outerRadius * 0.55;

  let startAngle = -Math.PI / 2;
  const legend = document.createElement('div');
  legend.className = 'chart-legend';

  entries.forEach(([label, value], i) => {
    const fraction = total > 0 ? value / total : 0;
    const endAngle = startAngle + fraction * Math.PI * 2;
    const color = CHART_PALETTE[i % CHART_PALETTE.length];

    ctx.beginPath();
    ctx.moveTo(cx, cy);
    ctx.arc(cx, cy, outerRadius, startAngle, endAngle);
    ctx.closePath();
    ctx.fillStyle = color;
    ctx.fill();

    startAngle = endAngle;

    const item = document.createElement('div');
    item.className = 'chart-legend-item';
    const swatch = document.createElement('span');
    swatch.className = 'chart-legend-swatch';
    swatch.style.background = color;
    const text = document.createElement('span');
    text.textContent = `${label}: ${value} (${(fraction * 100).toFixed(0)}%)`;
    item.append(swatch, text);
    legend.appendChild(item);
  });

  // Cut out the inner circle to turn the pie into a donut, filling with the panel background
  // color so it blends with the surrounding card.
  ctx.beginPath();
  ctx.arc(cx, cy, innerRadius, 0, Math.PI * 2);
  ctx.fillStyle = cssVar('--panel', '#1e293b');
  ctx.fill();

  const wrapper = document.createElement('div');
  wrapper.className = 'chart-donut-wrapper';
  wrapper.append(canvas, legend);
  container.replaceChildren(wrapper);
}

/**
 * Renders a simple bar chart of `entries` (array of [label, value], e.g. dates and counts) into
 * the given container, drawn on a <canvas>.
 */
function renderBarChart(container, entries) {
  container.replaceChildren();

  if (!entries.length) {
    container.append('Nenhum dado disponível ainda.');
    return;
  }

  const width = Math.max(320, Math.min(760, entries.length * 48));
  const height = 220;
  const padding = { top: 10, right: 10, bottom: 36, left: 34 };
  const dpr = window.devicePixelRatio || 1;

  const canvas = document.createElement('canvas');
  canvas.width = width * dpr;
  canvas.height = height * dpr;
  canvas.style.width = `${width}px`;
  canvas.style.height = `${height}px`;
  canvas.className = 'chart-canvas';

  const ctx = canvas.getContext('2d');
  ctx.scale(dpr, dpr);

  const maxValue = Math.max(...entries.map(([, v]) => v), 1);
  const plotWidth = width - padding.left - padding.right;
  const plotHeight = height - padding.top - padding.bottom;
  const barGap = 8;
  const barWidth = Math.max(4, plotWidth / entries.length - barGap);

  const gridColor = cssVar('--panel-light', '#334155');
  const barColor = cssVar('--accent', '#38bdf8');
  const textColor = cssVar('--muted', '#94a3b8');

  // Baseline.
  ctx.strokeStyle = gridColor;
  ctx.beginPath();
  ctx.moveTo(padding.left, padding.top + plotHeight);
  ctx.lineTo(padding.left + plotWidth, padding.top + plotHeight);
  ctx.stroke();

  ctx.fillStyle = textColor;
  ctx.font = '10px system-ui, sans-serif';
  ctx.textAlign = 'center';

  entries.forEach(([label, value], i) => {
    const barHeight = (value / maxValue) * (plotHeight - 8);
    const x = padding.left + i * (plotWidth / entries.length) + barGap / 2;
    const y = padding.top + plotHeight - barHeight;

    ctx.fillStyle = barColor;
    ctx.fillRect(x, y, barWidth, barHeight);

    ctx.fillStyle = textColor;
    ctx.fillText(String(value), x + barWidth / 2, y - 4 < padding.top ? padding.top + 10 : y - 4);

    const shortLabel = label.length > 6 ? label.slice(5) : label; // e.g. "2026-07-01" -> "07-01"
    ctx.save();
    ctx.translate(x + barWidth / 2, padding.top + plotHeight + 14);
    ctx.rotate(-Math.PI / 6);
    ctx.textAlign = 'right';
    ctx.fillText(shortLabel, 0, 0);
    ctx.restore();
  });

  container.appendChild(canvas);
}
