import { Link } from "react-router-dom";
import { styled, Box, Typography } from "@mui/material";
import { IconAtom } from "@tabler/icons-react";

const LinkStyled = styled(Link)(() => ({
  height: "70px",
  width: "180px",
  overflow: "hidden",
  display: "block",
}));

const Logo = () => {
  return (
    <LinkStyled
      to="/"
      height={70}
      style={{
        display: "flex",
        alignItems: "center",
      }}
    >
      <Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
        <IconAtom size={36} color="#5D87FF" />
        <Typography variant="h5" fontWeight={700} color="text.primary">
          Momentum
        </Typography>
      </Box>
    </LinkStyled>
  );
};

export default Logo;
