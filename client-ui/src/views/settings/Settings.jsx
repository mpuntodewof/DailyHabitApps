import { useEffect, useState } from "react";
import api from "../../api/axiosInstance";
import TwoFactorDialog from "./TwoFactorDialog";
import TagManagement from "./TagManagement";
import {
  Box,
  Card,
  CardContent,
  Typography,
  Switch,
  List,
  ListItem,
  ListItemText,
  ListItemIcon,
  ListItemSecondaryAction,
  Divider,
  Button,
  Stack,
  Snackbar,
  Alert,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  RadioGroup,
  Radio,
  FormControlLabel,
  TextField,
  InputAdornment,
  Grid,
  Container
} from "@mui/material";
import { SketchPicker } from "react-color";
import PageContainer from "../../components/container/PageContainer";
import {
  IconBell,
  IconMoon,
  IconLock,
  IconLanguage,
  IconDevices,
  IconPalette,
  IconSpacingHorizontal,
} from "@tabler/icons-react";
import { useUserPreferences } from "../../context/UserPreferencesContext";

const Settings = () => {
  const { prefs, savePrefs } = useUserPreferences();

  const [twoFactorEnabled, setTwoFactorEnabled] = useState(false);
  const [twoFactorDialog, setTwoFactorDialog] = useState({ open: false, mode: 'enable' });
  const [colorPickerOpen, setColorPickerOpen] = useState(false);

  const refreshTwoFactor = async () => {
    try {
      const res = await api.get('/TwoFactor/status');
      setTwoFactorEnabled(!!res.data?.result?.enabled);
    } catch {
      setTwoFactorEnabled(false);
    }
  };

  useEffect(() => { refreshTwoFactor(); }, []);

  const handleTwoFactorToggle = (e) => {
    setTwoFactorDialog({ open: true, mode: e.target.checked ? 'enable' : 'disable' });
  };
  const [snackbar, setSnackbar] = useState({
    open: false,
    message: "",
    severity: "success",
  });

  const handleToggle = (key) => async (event) => {
    const ok = await savePrefs({ [key]: event.target.checked });
    setSnackbar({
      open: true,
      message: ok ? "Settings updated successfully!" : "Failed to save settings",
      severity: ok ? "success" : "error",
    });
  };

  const handleThemeChange = async (key, value) => {
    const numericKeys = ["borderRadius", "spacing"];
    const cast = numericKeys.includes(key) ? Number(value) || 0 : value;
    const ok = await savePrefs({ [key]: cast });
    setSnackbar({
      open: true,
      message: ok ? "Theme settings updated!" : "Failed to save theme",
      severity: ok ? "success" : "error",
    });
  };

  const handleColorPickerClose = () => {
    setColorPickerOpen(false);
  };

  const handleSnackbarClose = () => {
    setSnackbar({ ...snackbar, open: false });
  };

  return (
    <PageContainer title="Settings" description="User Settings Page">
      <Container maxWidth="lg">
        <Grid container spacing={3}>
          <Grid item width={"100%"}>
            <Card sx={{ width: '100%' }}>
              <CardContent>
                <Typography variant="h3" mb={3}>
                  General Settings
                </Typography>
                <List>
                  <ListItem>
                    <ListItemIcon>
                      <IconBell />
                    </ListItemIcon>
                    <ListItemText
                      primary="Notifications"
                      secondary="Enable notifications for updates and alerts"
                    />
                    <ListItemSecondaryAction>
                      <Switch
                        edge="end"
                        checked={!!prefs.notifications}
                        onChange={handleToggle("notifications")}
                      />
                    </ListItemSecondaryAction>
                  </ListItem>
                  <Divider />
                  <ListItem>
                    <ListItemIcon>
                      <IconMoon />
                    </ListItemIcon>
                    <ListItemText
                      primary="Dark Mode"
                      secondary="Switch between light and dark themes"
                    />
                    <ListItemSecondaryAction>
                      <Switch
                        edge="end"
                        checked={!!prefs.darkMode}
                        onChange={handleToggle("darkMode")}
                      />
                    </ListItemSecondaryAction>
                  </ListItem>
                  <Divider />
                  <ListItem>
                    <ListItemIcon>
                      <IconLock />
                    </ListItemIcon>
                    <ListItemText
                      primary="Two-Factor Authentication"
                      secondary={twoFactorEnabled ? "Enabled — toggle off to disable" : "Add an extra layer of security to your account"}
                    />
                    <ListItemSecondaryAction>
                      <Switch
                        edge="end"
                        checked={twoFactorEnabled}
                        onChange={handleTwoFactorToggle}
                      />
                    </ListItemSecondaryAction>
                  </ListItem>
                  <Divider />
                  <ListItem>
                    <ListItemIcon>
                      <IconLanguage />
                    </ListItemIcon>
                    <ListItemText
                      primary="Email Updates"
                      secondary="Receive email notifications for important updates"
                    />
                    <ListItemSecondaryAction>
                      <Switch
                        edge="end"
                        checked={!!prefs.emailUpdates}
                        onChange={handleToggle("emailUpdates")}
                      />
                    </ListItemSecondaryAction>
                  </ListItem>
                  <Divider />
                  <ListItem>
                    <ListItemIcon>
                      <IconDevices />
                    </ListItemIcon>
                    <ListItemText
                      primary="Device Sync"
                      secondary="Sync settings across all your devices"
                    />
                    <ListItemSecondaryAction>
                      <Switch
                        edge="end"
                        checked={!!prefs.deviceSync}
                        onChange={handleToggle("deviceSync")}
                      />
                    </ListItemSecondaryAction>
                  </ListItem>
                </List>
              </CardContent>
            </Card>
          </Grid>
          <Grid item width={"100%"}>
            <Card>
              <CardContent>
                <Typography variant="h3" mb={3}>
                  Theme Customization
                </Typography>
                <Stack spacing={3}>
                  <Box>
                    <Typography variant="subtitle1" gutterBottom>
                      Primary Color
                    </Typography>
                    <Stack direction="row" spacing={2} alignItems="center">
                      <Box
                        sx={{
                          width: 48,
                          height: 48,
                          borderRadius: 1,
                          bgcolor: prefs.primaryColor,
                          cursor: "pointer",
                          border: "2px solid",
                          borderColor: "divider",
                        }}
                        onClick={() => setColorPickerOpen(true)}
                      />
                      <Button
                        startIcon={<IconPalette />}
                        variant="outlined"
                        onClick={() => setColorPickerOpen(true)}
                      >
                        Change Color
                      </Button>
                    </Stack>
                  </Box>

                  <Box>
                    <Typography variant="subtitle1" gutterBottom>
                      Font Family
                    </Typography>
                    <RadioGroup
                      value={prefs.fontFamily}
                      onChange={(e) =>
                        handleThemeChange("fontFamily", e.target.value)
                      }
                    >
                      <Stack direction="row" spacing={2}>
                        <FormControlLabel
                          value="Inter"
                          control={<Radio />}
                          label="Inter"
                        />
                        <FormControlLabel
                          value="Roboto"
                          control={<Radio />}
                          label="Roboto"
                        />
                        <FormControlLabel
                          value="Poppins"
                          control={<Radio />}
                          label="Poppins"
                        />
                      </Stack>
                    </RadioGroup>
                  </Box>

                  <Box>
                    <Typography variant="subtitle1" gutterBottom>
                      Border Radius (px)
                    </Typography>
                    <TextField
                      type="number"
                      value={prefs.borderRadius}
                      onChange={(e) =>
                        handleThemeChange("borderRadius", e.target.value)
                      }
                      InputProps={{
                        startAdornment: (
                          <InputAdornment position="start">
                            <IconSpacingHorizontal />
                          </InputAdornment>
                        ),
                      }}
                      sx={{ width: 200 }}
                    />
                  </Box>

                  <Box>
                    <Typography variant="subtitle1" gutterBottom>
                      Base Spacing (px)
                    </Typography>
                    <TextField
                      type="number"
                      value={prefs.spacing}
                      onChange={(e) => handleThemeChange("spacing", e.target.value)}
                      InputProps={{
                        startAdornment: (
                          <InputAdornment position="start">
                            <IconSpacingHorizontal />
                          </InputAdornment>
                        ),
                      }}
                      sx={{ width: 200 }}
                    />
                  </Box>
                </Stack>

              </CardContent>
            </Card>
          </Grid>
          <Grid item width={"100%"}>
            <TagManagement />
          </Grid>
        </Grid>

        <Dialog
          open={colorPickerOpen}
          onClose={handleColorPickerClose}
          maxWidth="xs"
          fullWidth
        >
          <DialogTitle>Choose Primary Color</DialogTitle>
          <DialogContent>
            <Box sx={{ p: 2 }}>
              <SketchPicker
                color={prefs.primaryColor}
                onChange={(color) => handleThemeChange("primaryColor", color.hex)}
                width="100%"
              />
            </Box>
          </DialogContent>
          <DialogActions>
            <Button onClick={handleColorPickerClose}>Done</Button>
          </DialogActions>
        </Dialog>

        <Snackbar
          open={snackbar.open}
          autoHideDuration={6000}
          onClose={handleSnackbarClose}
          anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        >
          <Alert
            onClose={handleSnackbarClose}
            severity={snackbar.severity}
            variant="filled"
            sx={{ width: '100%' }}
          >
            {snackbar.message}
          </Alert>
        </Snackbar>

        <TwoFactorDialog
          open={twoFactorDialog.open}
          mode={twoFactorDialog.mode}
          onClose={() => setTwoFactorDialog({ ...twoFactorDialog, open: false })}
          onChanged={refreshTwoFactor}
        />
      </Container>
    </PageContainer>
  );
};

export default Settings;
