import './App.css'
import { CssBaseline, ThemeProvider } from '@mui/material';
import { buildAppTheme } from './theme/DefaultColors';
import { RouterProvider } from 'react-router';
import router from './routes/Router'
import { HabitProvider } from './context/HabitContext';
import { AuthProvider } from './context/AuthContext';
import { SnackbarProvider } from './context/SnackbarContext';
import { HabitTrackingProvider } from './context/HabitTrackingContext';
import { UserPreferencesProvider, useUserPreferences } from './context/UserPreferencesContext';
import { TagProvider } from './context/TagContext';
import { GoalProvider } from './context/GoalContext';

const ThemedRoutes = () => {
  const { prefs } = useUserPreferences();
  const theme = buildAppTheme(prefs?.darkMode ? 'dark' : 'light');

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <TagProvider>
        <GoalProvider>
          <HabitProvider>
            <HabitTrackingProvider>
              <RouterProvider router={router} />
            </HabitTrackingProvider>
          </HabitProvider>
        </GoalProvider>
      </TagProvider>
    </ThemeProvider>
  );
};

function App() {
  return (
    <ThemeProvider theme={buildAppTheme('light')}>
      <CssBaseline />
      <SnackbarProvider>
        <AuthProvider>
          <UserPreferencesProvider>
            <ThemedRoutes />
          </UserPreferencesProvider>
        </AuthProvider>
      </SnackbarProvider>
    </ThemeProvider>
  );
}

export default App
