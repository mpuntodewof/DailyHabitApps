import React, { useMemo } from 'react';
import { Card, CardContent, Typography, Box, Tooltip, Stack } from '@mui/material';

/*
 * GitHub-style contribution heatmap.
 *
 * Renders the trailing year of habit-completion cells as a grid of small
 * rounded squares: columns = weeks (oldest → newest), rows = day-of-week
 * (Sun→Sat). Colour intensity is the backend's 0–4 bucket, mapped to an
 * ascending green scale (GitHub's signature look). Month labels run along the
 * top; a hover tooltip shows the date + completion count.
 *
 * Props:
 *   cells: Array<{ date: "yyyy-MM-dd", count: number, intensity: 0..4 }>
 */

// Ascending green scale (level 0 = empty). Mirrors GitHub's contribution colors,
// tuned slightly toward the app's mint (#13DEB9) accent at the top end.
const LEVEL_COLORS = ['#ebedf0', '#9be9a8', '#40c463', '#30a14e', '#216e39'];
const CELL = 12;   // square size (px)
const GAP = 3;     // gap between squares (px)
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const DAY_LABELS = ['', 'Mon', '', 'Wed', '', 'Fri', '']; // GitHub shows Mon/Wed/Fri only

/**
 * Bucket cells into week-columns. Each column is a 7-slot array (index =
 * day-of-week, 0=Sun). Leading slots before the first cell's weekday are null
 * (blank squares), so the grid aligns to real calendar weeks like GitHub's.
 */
function buildColumns(cells) {
  if (!cells.length) return { columns: [], monthSpans: [] };

  // sort ascending by date defensively
  const sorted = [...cells].sort((a, b) => a.date.localeCompare(b.date));

  const columns = [];
  let current = new Array(7).fill(null);
  let started = false;

  for (const cell of sorted) {
    const dow = new Date(cell.date + 'T00:00:00').getDay(); // 0=Sun..6=Sat
    if (!started) {
      // pad the first column so the first cell lands on its real weekday
      current = new Array(7).fill(null);
      started = true;
    }
    current[dow] = cell;
    if (dow === 6) {
      columns.push(current);
      current = new Array(7).fill(null);
    }
  }
  // push the trailing partial week if it has any cells
  if (current.some((c) => c !== null)) columns.push(current);

  // Month labels: for each column, the month of its first real cell; emit a
  // label only when the month changes from the previous column.
  const monthSpans = [];
  let lastMonth = -1;
  columns.forEach((col, i) => {
    const firstCell = col.find((c) => c);
    if (!firstCell) return;
    const m = new Date(firstCell.date + 'T00:00:00').getMonth();
    if (m !== lastMonth) {
      monthSpans.push({ colIndex: i, label: MONTHS[m] });
      lastMonth = m;
    }
  });

  return { columns, monthSpans };
}

const ContributionHeatmap = ({ cells = [] }) => {
  const { columns, monthSpans } = useMemo(() => buildColumns(cells), [cells]);

  const totalCompletions = useMemo(
    () => cells.reduce((sum, c) => sum + (c.count || 0), 0),
    [cells]
  );

  const colStride = CELL + GAP;

  return (
    <Card sx={{ width: '100%' }}>
      <CardContent>
        <Stack direction="row" justifyContent="space-between" alignItems="baseline" sx={{ mb: 2, flexWrap: 'wrap', gap: 1 }}>
          <Typography variant="h5">Activity</Typography>
          <Typography variant="body2" color="text.secondary">
            {totalCompletions} completion{totalCompletions === 1 ? '' : 's'} in the last year
          </Typography>
        </Stack>

        {columns.length === 0 ? (
          <Typography variant="body2" color="text.secondary">
            No activity yet — complete a habit to start your graph.
          </Typography>
        ) : (
          <Box sx={{ overflowX: 'auto', pb: 1 }}>
            <Box sx={{ display: 'inline-block', minWidth: 'min-content' }}>
              {/* Month labels row */}
              <Box sx={{ position: 'relative', height: 16, ml: `${28}px`, mb: '2px' }}>
                {monthSpans.map((m) => (
                  <Typography
                    key={`${m.label}-${m.colIndex}`}
                    variant="caption"
                    sx={{
                      position: 'absolute',
                      left: `${m.colIndex * colStride}px`,
                      fontSize: 11,
                      color: 'text.secondary',
                      whiteSpace: 'nowrap',
                    }}
                  >
                    {m.label}
                  </Typography>
                ))}
              </Box>

              {/* Day labels (left) + grid */}
              <Box sx={{ display: 'flex' }}>
                {/* day-of-week labels */}
                <Box sx={{ display: 'flex', flexDirection: 'column', width: 28, mr: '0px' }}>
                  {DAY_LABELS.map((label, i) => (
                    <Box key={i} sx={{ height: `${CELL}px`, mb: `${GAP}px`, display: 'flex', alignItems: 'center' }}>
                      <Typography variant="caption" sx={{ fontSize: 10, color: 'text.secondary', lineHeight: 1 }}>
                        {label}
                      </Typography>
                    </Box>
                  ))}
                </Box>

                {/* week columns */}
                <Box sx={{ display: 'flex', gap: `${GAP}px` }}>
                  {columns.map((col, ci) => (
                    <Box key={ci} sx={{ display: 'flex', flexDirection: 'column', gap: `${GAP}px` }}>
                      {col.map((cell, ri) => {
                        if (!cell) {
                          return <Box key={ri} sx={{ width: CELL, height: CELL }} />;
                        }
                        const color = LEVEL_COLORS[Math.min(cell.intensity ?? 0, 4)];
                        const label = cell.count > 0
                          ? `${cell.count} completion${cell.count === 1 ? '' : 's'} on ${cell.date}`
                          : `No completions on ${cell.date}`;
                        return (
                          <Tooltip key={ri} title={label} arrow placement="top">
                            <Box
                              sx={{
                                width: CELL,
                                height: CELL,
                                borderRadius: '2px',
                                backgroundColor: color,
                                outline: '1px solid rgba(27,31,35,0.06)',
                                outlineOffset: '-1px',
                                cursor: 'default',
                                transition: 'transform .1s ease',
                                '&:hover': { transform: 'scale(1.25)' },
                              }}
                            />
                          </Tooltip>
                        );
                      })}
                    </Box>
                  ))}
                </Box>
              </Box>

              {/* Legend */}
              <Stack direction="row" alignItems="center" spacing={0.5} sx={{ mt: 1.5, justifyContent: 'flex-end' }}>
                <Typography variant="caption" sx={{ fontSize: 10, color: 'text.secondary', mr: 0.5 }}>Less</Typography>
                {LEVEL_COLORS.map((c, i) => (
                  <Box key={i} sx={{ width: CELL, height: CELL, borderRadius: '2px', backgroundColor: c, outline: '1px solid rgba(27,31,35,0.06)', outlineOffset: '-1px' }} />
                ))}
                <Typography variant="caption" sx={{ fontSize: 10, color: 'text.secondary', ml: 0.5 }}>More</Typography>
              </Stack>
            </Box>
          </Box>
        )}
      </CardContent>
    </Card>
  );
};

export default ContributionHeatmap;
