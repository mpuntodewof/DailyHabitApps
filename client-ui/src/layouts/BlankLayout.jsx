import { Outlet } from "react-router";
import { Link as RouterLink } from "react-router-dom";
import { Button } from "@mui/material";
import { IconArrowLeft } from "@tabler/icons-react";

const BlankLayout = () => (
  <>
    {/* Back to the public landing page — shown on every auth / blank screen. */}
    <Button
      component={RouterLink}
      to="/"
      startIcon={<IconArrowLeft size={18} />}
      sx={{
        position: "fixed",
        top: 20,
        left: 20,
        zIndex: 1200,
        textTransform: "none",
        fontWeight: 600,
        color: "text.primary",
        backgroundColor: "rgba(255,255,255,0.7)",
        backdropFilter: "blur(8px)",
        borderRadius: "999px",
        px: 2,
        boxShadow: "0 4px 14px -6px rgba(0,0,0,0.25)",
        "&:hover": { backgroundColor: "rgba(255,255,255,0.95)" },
      }}
    >
      Back to home
    </Button>
    <Outlet />
  </>
);

export default BlankLayout;
