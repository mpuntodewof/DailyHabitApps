import React, { useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { Box, Container, Typography, Button, Stack, Chip, Paper, Grid, GlobalStyles } from '@mui/material';

/*
 * Landing — public marketing page at "/".
 * Rebuilt in MUI from the standalone landing/index.html, faithful to the
 * approved "calm momentum" design: warm paper background, periwinkle (#5D87FF)
 * primary + mint (#13DEB9) "done" accent, Fraunces display + Hanken Grotesk body,
 * an animated habit heatmap, and one-time $29 Free/Pro pricing.
 *
 * CTAs route into the app's auth flow. This page is intentionally self-styled
 * (it does not depend on the app's MUI theme) so it reads as a marketing page.
 */

// ---- palette (carried from theme/DefaultColors.js + the landing design) ----
const C = {
  brand: '#5D87FF',
  brandDeep: '#4570EA',
  streak: '#13DEB9',
  streakDeep: '#02b3a9',
  paper: '#F7F4EE',
  paper2: '#FBF9F4',
  paperEdge: '#EAE3D6',
  ink: '#20242E',
  inkSoft: '#5A6175',
  inkFaint: '#8A8F9E',
};
const SERIF = '"Fraunces", Georgia, serif';
const SANS = '"Hanken Grotesk", system-ui, sans-serif';

// Keyframes + a few effects that are awkward in pure sx, injected once.
const globalCss = `
  @keyframes mm-rise { to { opacity: 1; transform: none; } }
  @keyframes mm-pop  { from { transform: scale(0.4); opacity: 0; } to { transform: scale(1); opacity: 1; } }
  @keyframes mm-ul   { to { transform: scaleX(1); } }
  .mm-reveal { opacity: 0; transform: translateY(18px); animation: mm-rise .9s cubic-bezier(.22,1,.36,1) forwards; }
  @media (prefers-reduced-motion: reduce) { .mm-reveal, .mm-cell { animation: none !important; opacity: 1 !important; transform: none !important; } }
`;

const Check = () => (
  <Box component="svg" viewBox="0 0 24 24"
    sx={{ width: 19, height: 19, flex: '0 0 auto', mt: '2px', stroke: C.streakDeep, fill: 'none', strokeWidth: 2.2, strokeLinecap: 'round', strokeLinejoin: 'round' }}>
    <path d="M5 12l5 5L20 7" />
  </Box>
);

const FeatureIcon = ({ children }) => (
  <Box sx={{ width: 46, height: 46, borderRadius: '13px', display: 'grid', placeItems: 'center', background: 'rgba(93,135,255,0.12)', mb: 2.25 }}>
    <Box component="svg" viewBox="0 0 24 24"
      sx={{ width: 24, height: 24, stroke: C.brandDeep, fill: 'none', strokeWidth: 1.7, strokeLinecap: 'round', strokeLinejoin: 'round' }}>
      {children}
    </Box>
  </Box>
);

const FEATURES = [
  { icon: <path d="M12 3v18M3 12h18" />, title: 'One-tap daily check-in', body: 'Log a habit in a second. Set a goal — pages, minutes, reps — or just mark it done. Daily, weekly, or monthly cadence.' },
  { icon: <><rect x="3" y="3" width="7" height="7" rx="1.5" /><rect x="14" y="3" width="7" height="7" rx="1.5" /><rect x="3" y="14" width="7" height="7" rx="1.5" /><rect x="14" y="14" width="7" height="7" rx="1.5" /></>, title: 'The momentum heatmap', body: 'See your whole year as a field of filled-in days. The grid is the reward — and the gentle nudge when a column goes blank.' },
  { icon: <><path d="M3 17l6-6 4 4 7-7" /><path d="M21 8V3h-5" /></>, title: 'Streaks & real stats', body: "Current and best streaks, completion rates, and weekly/monthly/yearly breakdowns — math that respects each habit's own cadence." },
  { icon: <><path d="M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9" /><path d="M13.7 21a2 2 0 0 1-3.4 0" /></>, title: 'Reminders that respect you', body: "Per-habit reminders on the days you choose. A nudge, not a nag — and never a notification you didn't ask for.", pro: true },
  { icon: <><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><path d="M14 2v6h6M12 18v-6M9 15h6" /></>, title: 'Own your data', body: "Export everything to CSV or JSON whenever you want. Delete your account and it's gone. No lock-in, no games.", pro: true },
  { icon: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 3" /></>, title: 'Tags, themes & dark mode', body: 'Group habits with tags, filter and search, and make it yours with custom themes and a proper dark mode.', pro: true },
];

const PRO_FEATURES = [
  <><b>Unlimited</b> habits</>,
  'Full-year heatmap & deep analytics',
  'Reminders on the days you choose',
  'Tags, custom themes & dark mode',
  'CSV / JSON export — your data, always',
];
const FREE_FEATURES = ['Track up to 3 habits', 'Daily check-ins & streaks', '30-day heatmap', 'Secure account, 2FA'];

const FAQS = [
  { q: 'Is it really a one-time payment?', a: "Yes. Pay $29 once and Pro is yours — including future updates. No recurring charge, ever. A habit tracker shouldn't cost you a subscription for the rest of your life." },
  { q: 'What happens to my data?', a: "It's yours. You can export everything to CSV or JSON at any time, and deleting your account permanently removes your data. No ads, no selling data, no tracking pixels." },
  { q: 'Can I try it before paying?', a: 'Absolutely — the Free tier lets you track up to 3 habits with streaks and a 30-day heatmap. Upgrade to Pro only if it earns a place in your day.' },
  { q: 'What if I change my mind?', a: "There's a 14-day, no-questions money-back guarantee. If it isn't for you, email me and I'll refund you." },
];

// Build a deterministic heatmap (later cells trend fuller).
function useHeatLevels() {
  return useMemo(() => {
    const COLS = 13, ROWS = 4, total = COLS * ROWS, out = [];
    for (let i = 0; i < total; i++) {
      const recency = i / total;
      const seed = (Math.sin(i * 12.9898) * 43758.5453) % 1;
      const r = Math.abs(seed) * 0.7 + recency * 0.4;
      out.push(r < 0.35 ? 0 : r < 0.55 ? 1 : r < 0.72 ? 2 : r < 0.88 ? 3 : 4);
    }
    return out;
  }, []);
}

const HEAT_BG = ['rgba(42,53,71,0.07)', 'rgba(19,222,185,0.30)', 'rgba(19,222,185,0.55)', 'rgba(19,222,185,0.80)', C.streak];

const Landing = () => {
  const navigate = useNavigate();
  const levels = useHeatLevels();
  const goRegister = () => navigate('/auth/register');
  const goLogin = () => navigate('/auth/login');

  const sectionLabel = { fontFamily: SANS, fontSize: '.8rem', fontWeight: 600, letterSpacing: '.1em', textTransform: 'uppercase', color: C.brandDeep };
  const sectionTitle = { fontFamily: SERIF, fontWeight: 500, letterSpacing: '-0.02em', fontSize: 'clamp(2rem, 4vw, 3rem)', lineHeight: 1.08, mt: 1.5, color: C.ink };

  return (
    <Box sx={{
      fontFamily: SANS, color: C.ink, minHeight: '100vh', overflowX: 'hidden',
      backgroundColor: C.paper,
      backgroundImage: 'radial-gradient(120% 80% at 88% -10%, rgba(93,135,255,0.10), transparent 55%), radial-gradient(90% 60% at -10% 8%, rgba(19,222,185,0.10), transparent 50%)',
    }}>
      <style>{globalCss}</style>
      {/* Paint the document body the same paper colour so overscroll / any gap
          below the footer doesn't flash the browser's default white. Scoped to
          this route — GlobalStyles is removed when the Landing page unmounts. */}
      <GlobalStyles styles={{ 'html, body': { backgroundColor: C.paper } }} />

      {/* NAV */}
      <Box component="nav" sx={{ position: 'sticky', top: 0, zIndex: 50, backdropFilter: 'blur(10px)', background: 'rgba(247,244,238,0.72)', borderBottom: '1px solid rgba(234,227,214,0.8)' }}>
        <Container maxWidth="lg" sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', height: 68 }}>
          <Stack direction="row" alignItems="center" spacing={1.4} sx={{ fontWeight: 600, letterSpacing: '-0.01em', fontSize: '1.12rem' }}>
            <Box sx={{ width: 26, height: 26, borderRadius: '8px', background: `linear-gradient(135deg, ${C.brand}, ${C.streak})`, position: 'relative', boxShadow: '0 6px 16px -6px rgba(93,135,255,0.7)', '&::after': { content: '""', position: 'absolute', inset: '7px', borderRadius: '3px', background: C.paper2 } }} />
            <span>Momentum</span>
          </Stack>
          <Button onClick={goRegister} disableElevation sx={{ fontFamily: SANS, fontWeight: 600, fontSize: '.92rem', px: 2.25, py: 1, borderRadius: '999px', background: C.ink, color: C.paper2, textTransform: 'none', '&:hover': { background: C.brandDeep } }}>
            Get Momentum — $29
          </Button>
        </Container>
      </Box>

      {/* HERO */}
      <Box component="header" sx={{ pt: { xs: 7, md: 11 }, pb: 5 }}>
        <Container maxWidth="lg">
          <Grid container spacing={{ xs: 5, md: 7 }} alignItems="center">
            <Grid item xs={12} md={7}>
              <Chip className="mm-reveal" label="● Built on the 1% rule"
                sx={{ '& .MuiChip-label': { px: 1.75 }, fontFamily: SANS, fontSize: '.78rem', fontWeight: 600, letterSpacing: '.04em', textTransform: 'uppercase', color: C.brandDeep, background: 'rgba(93,135,255,0.10)', border: '1px solid rgba(93,135,255,0.18)', height: 'auto', py: 0.7 }} />
              <Typography component="h1" className="mm-reveal" sx={{ animationDelay: '.1s', fontFamily: SERIF, fontWeight: 500, fontSize: 'clamp(2.7rem, 6.5vw, 5rem)', lineHeight: 1.02, letterSpacing: '-0.02em', mt: 2.5, maxWidth: '16ch', color: C.ink }}>
                The small things, <Box component="em" sx={{ fontStyle: 'italic', color: C.brandDeep }}>compounded</Box> into a{' '}
                <Box component="span" sx={{ position: 'relative', whiteSpace: 'nowrap', '&::after': { content: '""', position: 'absolute', left: 0, right: 0, bottom: '.08em', height: '.14em', background: C.streak, borderRadius: '2px', opacity: .55, transform: 'scaleX(0)', transformOrigin: 'left', animation: 'mm-ul 1s cubic-bezier(.22,1,.36,1) .8s forwards' } }}>life you like</Box>.
              </Typography>
              <Typography className="mm-reveal" sx={{ animationDelay: '.2s', fontFamily: SANS, fontSize: 'clamp(1.05rem, 1.7vw, 1.28rem)', color: C.inkSoft, maxWidth: '46ch', mt: 3.25 }}>
                A calm, focused habit tracker. No streaks-as-anxiety, no gamified noise — just the quiet satisfaction of showing up, one day at a time, and watching it add up.
              </Typography>
              <Stack className="mm-reveal" direction="row" flexWrap="wrap" spacing={1.75} sx={{ animationDelay: '.3s', mt: 4.25, gap: 1.75 }}>
                <Button onClick={goRegister} disableElevation sx={{ fontFamily: SANS, fontWeight: 600, fontSize: '1.02rem', px: 3.5, py: 1.75, borderRadius: '14px', color: '#fff', textTransform: 'none', background: `linear-gradient(135deg, ${C.brand}, ${C.brandDeep})`, boxShadow: '0 16px 34px -14px rgba(69,112,234,0.75)', '&:hover': { boxShadow: '0 22px 44px -14px rgba(69,112,234,0.85)' } }}>
                  Get Momentum — $29
                </Button>
                <Button href="#how" sx={{ fontFamily: SANS, fontWeight: 600, fontSize: '1rem', px: 2.75, py: 1.75, borderRadius: '14px', color: C.ink, textTransform: 'none', background: 'transparent', border: `1px solid ${C.paperEdge}`, '&:hover': { borderColor: C.brand, background: 'transparent' } }}>
                  See how it works
                </Button>
              </Stack>
              <Typography className="mm-reveal" sx={{ animationDelay: '.3s', mt: 2, fontFamily: SANS, fontSize: '.9rem', color: C.inkFaint }}>
                <Box component="b" sx={{ color: C.ink, fontWeight: 600 }}>One-time payment.</Box> No subscription. Yours forever, with free updates.
              </Typography>
            </Grid>

            {/* Signature visual: the living habit grid */}
            <Grid item xs={12} md={5}>
              <Paper elevation={0} className="mm-reveal" aria-hidden="true" sx={{ animationDelay: '.45s', background: C.paper2, border: `1px solid ${C.paperEdge}`, borderRadius: '22px', p: 2.75, boxShadow: '0 24px 60px -30px rgba(32,36,46,0.45)', transform: 'rotate(1.2deg)' }}>
                <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ mb: 2.25 }}>
                  <Box>
                    <Typography sx={{ fontFamily: SANS, fontWeight: 600, fontSize: '.95rem', color: C.ink }}>This month</Typography>
                    <Typography sx={{ fontFamily: SANS, fontSize: '.78rem', color: C.inkFaint }}>21 of 28 days · 75%</Typography>
                  </Box>
                  <Box sx={{ fontFamily: SANS, fontSize: '.78rem', fontWeight: 600, color: C.streakDeep, background: 'rgba(19,222,185,0.14)', px: 1.4, py: 0.6, borderRadius: '999px' }}>🔥 12-day streak</Box>
                </Stack>
                <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(13, 1fr)', gap: '6px' }}>
                  {levels.map((lvl, i) => (
                    <Box key={i} className="mm-cell" sx={{ aspectRatio: '1', borderRadius: '5px', background: HEAT_BG[lvl], animation: 'mm-pop .5s cubic-bezier(.22,1,.36,1) both', animationDelay: `${0.5 + i * 0.012}s` }} />
                  ))}
                </Box>
                <Stack spacing={1.25} sx={{ mt: 2.25 }}>
                  {[{ n: 'Read 10 pages', m: '12d', done: true }, { n: 'Morning walk', m: '8d', done: true }, { n: 'No phone before noon', m: '3d', done: false }].map((h) => (
                    <Stack key={h.n} direction="row" alignItems="center" spacing={1.5} sx={{ background: C.paper, border: `1px solid ${C.paperEdge}`, borderRadius: '12px', px: 1.6, py: 1.4 }}>
                      <Box sx={{ width: 22, height: 22, borderRadius: '7px', flex: '0 0 auto', border: h.done ? `2px solid ${C.streak}` : `2px solid ${C.paperEdge}`, background: h.done ? C.streak : 'transparent', position: 'relative', '&::after': h.done ? { content: '""', position: 'absolute', left: '6px', top: '2px', width: '5px', height: '10px', border: 'solid #fff', borderWidth: '0 2px 2px 0', transform: 'rotate(45deg)' } : {} }} />
                      <Typography sx={{ fontFamily: SANS, fontWeight: 500, fontSize: '.92rem', color: h.done ? C.inkSoft : C.ink }}>{h.n}</Typography>
                      <Typography sx={{ ml: 'auto', fontFamily: SANS, fontSize: '.76rem', color: C.inkFaint, fontVariantNumeric: 'tabular-nums' }}>{h.m}</Typography>
                    </Stack>
                  ))}
                </Stack>
              </Paper>
            </Grid>
          </Grid>
        </Container>
      </Box>

      {/* PROOF STRIP */}
      <Container maxWidth="lg" sx={{ py: 2 }}>
        <Stack direction="row" flexWrap="wrap" sx={{ gap: '14px 40px', alignItems: 'center', color: C.inkFaint, fontSize: '.86rem', fontFamily: SANS }}>
          <span>🔒 <b style={{ color: C.ink }}>Your data stays yours</b> — full export, delete anytime</span>
          <span>✨ <b style={{ color: C.ink }}>No ads, no tracking, no upsells</b></span>
          <span>📊 <b style={{ color: C.ink }}>Heatmaps, streaks &amp; real analytics</b></span>
        </Stack>
      </Container>

      {/* FEATURES */}
      <Box component="section" id="how" sx={{ py: { xs: 8, md: 11 } }}>
        <Container maxWidth="lg">
          <Typography sx={sectionLabel}>Why it sticks</Typography>
          <Typography sx={{ ...sectionTitle, maxWidth: '18ch' }}>Designed to make the next day the easy choice.</Typography>
          <Typography sx={{ fontFamily: SANS, color: C.inkSoft, maxWidth: '52ch', mt: 2, fontSize: '1.05rem' }}>
            Every feature exists to lower the friction of showing up — and to make the progress impossible to ignore.
          </Typography>
          {/* CSS Grid with equal columns + equal rows -> every card is identical size,
              regardless of text length or the PRO badge. */}
          <Box sx={{ mt: 3.5, display: 'grid', gap: 2.75, gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', md: 'repeat(3, 1fr)' }, gridAutoRows: '1fr' }}>
            {FEATURES.map((f) => (
              <Paper key={f.title} elevation={0} sx={{ height: '100%', display: 'flex', flexDirection: 'column', background: C.paper2, border: `1px solid ${C.paperEdge}`, borderRadius: '18px', p: 3.5, boxShadow: '0 18px 40px -22px rgba(32,36,46,0.35)', transition: 'transform .35s cubic-bezier(.22,1,.36,1), border-color .35s', '&:hover': { transform: 'translateY(-4px)', borderColor: 'rgba(93,135,255,0.4)' } }}>
                <FeatureIcon>{f.icon}</FeatureIcon>
                <Typography sx={{ fontFamily: SERIF, fontWeight: 500, fontSize: '1.32rem', mb: 1, letterSpacing: '-0.01em', color: C.ink }}>{f.title}</Typography>
                <Typography sx={{ fontFamily: SANS, color: C.inkSoft, fontSize: '.97rem' }}>{f.body}</Typography>
                {/* mt:auto pins the PRO badge to the bottom so badged/un-badged cards align */}
                {f.pro && <Box component="span" sx={{ alignSelf: 'flex-start', mt: 'auto', pt: 1.75, fontFamily: SANS, fontSize: '.72rem', fontWeight: 700, letterSpacing: '.06em', color: C.streakDeep }}><Box component="span" sx={{ background: 'rgba(19,222,185,0.14)', px: 1.25, py: 0.5, borderRadius: '999px' }}>PRO</Box></Box>}
              </Paper>
            ))}
          </Box>
        </Container>
      </Box>

      {/* PHILOSOPHY BAND */}
      <Container maxWidth="lg" sx={{ pb: { xs: 8, md: 11 } }}>
        <Box sx={{ background: C.ink, color: C.paper2, borderRadius: '28px', p: { xs: 5, md: 9.5 }, position: 'relative', overflow: 'hidden', '&::before': { content: '""', position: 'absolute', right: '-60px', top: '-60px', width: 320, height: 320, borderRadius: '50%', background: 'radial-gradient(circle, rgba(93,135,255,0.45), transparent 65%)' }, '&::after': { content: '""', position: 'absolute', left: '-40px', bottom: '-80px', width: 280, height: 280, borderRadius: '50%', background: 'radial-gradient(circle, rgba(19,222,185,0.40), transparent 65%)' } }}>
          <Typography sx={{ fontFamily: SERIF, fontWeight: 400, fontStyle: 'italic', fontSize: 'clamp(1.6rem, 3.4vw, 2.6rem)', lineHeight: 1.28, maxWidth: '22ch', position: 'relative', zIndex: 1, letterSpacing: '-0.01em' }}>
            “You do not rise to the level of your goals. You fall to the level of your <Box component="span" sx={{ color: C.streak, fontStyle: 'normal' }}>systems</Box>.”
          </Typography>
          <Typography sx={{ mt: 2.75, fontFamily: SANS, fontSize: '.92rem', color: 'rgba(247,244,238,0.65)', position: 'relative', zIndex: 1 }}>
            Momentum is a system for the 1% — the tiny, boring, daily reps that quietly become who you are.
          </Typography>
        </Box>
      </Container>

      {/* PRICING */}
      <Box component="section" id="pricing" sx={{ py: { xs: 8, md: 11 } }}>
        <Container maxWidth="lg">
          <Typography sx={sectionLabel}>Simple, honest pricing</Typography>
          <Typography sx={{ ...sectionTitle, maxWidth: '18ch' }}>Start free. Go Pro once, keep it forever.</Typography>
          <Typography sx={{ fontFamily: SANS, color: C.inkSoft, maxWidth: '52ch', mt: 2, fontSize: '1.05rem' }}>
            No monthly bill for a habit tracker. Try the free tier, and if it earns a place in your day, unlock everything with a single payment.
          </Typography>
          {/* Equal-width, equal-height CSS Grid tracks -> no vertical gap on the shorter card. */}
          <Box sx={{ mt: 3.5, display: 'grid', gap: 3, gridTemplateColumns: { xs: '1fr', md: 'repeat(2, 1fr)' }, gridAutoRows: '1fr', alignItems: 'stretch' }}>
            <Box>
              <Paper elevation={0} sx={{ height: '100%', background: C.paper2, border: `1px solid ${C.paperEdge}`, borderRadius: '20px', p: 4.25, display: 'flex', flexDirection: 'column' }}>
                <Typography sx={{ fontFamily: SERIF, fontWeight: 500, fontSize: '1.5rem', color: C.ink }}>Free</Typography>
                <Typography sx={{ fontFamily: SERIF, fontSize: '3rem', fontWeight: 500, mt: 1.75, letterSpacing: '-0.02em', color: C.ink }}>$0</Typography>
                <Typography sx={{ fontFamily: SANS, color: C.inkFaint, fontSize: '.9rem', mb: 2.75 }}>For getting started</Typography>
                <Stack spacing={1.6} sx={{ mb: 3.25 }}>
                  {FREE_FEATURES.map((t) => (<Stack key={t} direction="row" spacing={1.4} alignItems="flex-start"><Check /><Typography sx={{ fontFamily: SANS, fontSize: '.96rem', color: C.inkSoft }}>{t}</Typography></Stack>))}
                </Stack>
                <Box sx={{ mt: 'auto' }}>
                  <Button fullWidth onClick={goRegister} sx={{ fontFamily: SANS, fontWeight: 600, py: 1.75, borderRadius: '14px', color: C.ink, textTransform: 'none', border: `1px solid ${C.paperEdge}`, '&:hover': { borderColor: C.brand } }}>Start free</Button>
                </Box>
              </Paper>
            </Box>
            <Box>
              <Paper elevation={0} sx={{ height: '100%', background: `linear-gradient(180deg, #fff, ${C.paper2})`, border: `1.5px solid ${C.brand}`, borderRadius: '20px', p: 4.25, display: 'flex', flexDirection: 'column', position: 'relative', boxShadow: '0 30px 70px -34px rgba(69,112,234,0.6)' }}>
                <Box sx={{ position: 'absolute', top: 20, right: 20, fontFamily: SANS, fontSize: '.72rem', fontWeight: 700, letterSpacing: '.06em', color: '#fff', background: C.brand, px: 1.5, py: 0.6, borderRadius: '999px' }}>BEST VALUE</Box>
                <Typography sx={{ fontFamily: SERIF, fontWeight: 500, fontSize: '1.5rem', color: C.ink }}>Pro</Typography>
                <Typography sx={{ fontFamily: SERIF, fontSize: '3rem', fontWeight: 500, mt: 1.75, letterSpacing: '-0.02em', color: C.ink }}>$29 <Box component="small" sx={{ fontFamily: SANS, fontSize: '.95rem', fontWeight: 500, color: C.inkFaint }}>once</Box></Typography>
                <Typography sx={{ fontFamily: SANS, color: C.inkFaint, fontSize: '.9rem', mb: 2.75 }}>Everything, forever. Free updates.</Typography>
                <Stack spacing={1.6} sx={{ mb: 3.25 }}>
                  {PRO_FEATURES.map((t, i) => (<Stack key={i} direction="row" spacing={1.4} alignItems="flex-start"><Check /><Typography sx={{ fontFamily: SANS, fontSize: '.96rem', color: C.inkSoft }}>{t}</Typography></Stack>))}
                </Stack>
                <Box sx={{ mt: 'auto' }}>
                  <Button fullWidth onClick={goRegister} disableElevation sx={{ fontFamily: SANS, fontWeight: 600, fontSize: '1.02rem', py: 1.75, borderRadius: '14px', color: '#fff', textTransform: 'none', background: `linear-gradient(135deg, ${C.brand}, ${C.brandDeep})`, boxShadow: '0 16px 34px -14px rgba(69,112,234,0.75)' }}>Get Momentum — $29</Button>
                  <Typography sx={{ textAlign: 'center', fontFamily: SANS, fontSize: '.82rem', color: C.inkFaint, mt: 1.5 }}>One-time payment · 14-day money-back · No subscription</Typography>
                </Box>
              </Paper>
            </Box>
          </Box>
        </Container>
      </Box>

      {/* FAQ */}
      <Box component="section" sx={{ pb: { xs: 8, md: 11 } }}>
        <Container maxWidth="md">
          <Typography sx={{ ...sectionLabel, textAlign: 'center' }}>Questions</Typography>
          <Typography sx={{ ...sectionTitle, textAlign: 'center', mx: 'auto' }}>Good to know</Typography>
          <Box sx={{ mt: 4 }}>
            {FAQS.map((item) => (
              <Box key={item.q} component="details" sx={{ borderBottom: `1px solid ${C.paperEdge}`, '& summary': { cursor: 'pointer', listStyle: 'none', py: 2.75, px: 0.5, fontFamily: SANS, fontWeight: 600, fontSize: '1.05rem', display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 2, color: C.ink }, '& summary::-webkit-details-marker': { display: 'none' }, '& summary::after': { content: '"+"', fontSize: '1.5rem', color: C.brandDeep, fontWeight: 400 }, '&[open] summary::after': { content: '"–"' } }}>
                <summary>{item.q}</summary>
                <Typography sx={{ fontFamily: SANS, m: '0 4px', pb: 2.75, color: C.inkSoft, fontSize: '.98rem' }}>{item.a}</Typography>
              </Box>
            ))}
          </Box>
        </Container>
      </Box>

      {/* FOOTER */}
      <Box component="footer" sx={{ py: 7, borderTop: `1px solid ${C.paperEdge}` }}>
        <Container maxWidth="lg">
          <Stack direction="row" flexWrap="wrap" justifyContent="space-between" alignItems="center" sx={{ gap: 3 }}>
            <Stack direction="row" alignItems="center" spacing={1.4} sx={{ fontFamily: SANS, fontWeight: 600 }}>
              <Box sx={{ width: 26, height: 26, borderRadius: '8px', background: `linear-gradient(135deg, ${C.brand}, ${C.streak})`, position: 'relative', '&::after': { content: '""', position: 'absolute', inset: '7px', borderRadius: '3px', background: C.paper2 } }} />
              <span>Momentum</span>
            </Stack>
            <Stack direction="row" flexWrap="wrap" spacing={3} sx={{ fontFamily: SANS, fontSize: '.9rem', color: C.inkSoft, gap: 3 }}>
              <Box component="a" href="#how" sx={{ '&:hover': { color: C.brandDeep } }}>Features</Box>
              <Box component="a" href="#pricing" sx={{ '&:hover': { color: C.brandDeep } }}>Pricing</Box>
              <Box component="a" onClick={goLogin} sx={{ cursor: 'pointer', '&:hover': { color: C.brandDeep } }}>Sign in</Box>
            </Stack>
            <Typography sx={{ fontFamily: SANS, fontSize: '.82rem', color: C.inkFaint }}>© {new Date().getFullYear()} Momentum. Built for the 1%.</Typography>
          </Stack>
        </Container>
      </Box>
    </Box>
  );
}

export default Landing;
