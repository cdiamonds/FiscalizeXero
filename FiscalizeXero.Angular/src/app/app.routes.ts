import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { setupGuard } from './core/guards/setup.guard';
import { companyGuard } from './core/guards/company.guard';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./pages/landing/landing').then((m) => m.Landing)
  },
  {
    path: 'login',
    loadComponent: () => import('./pages/login/login').then((m) => m.Login)
  },
  {
    // Choose the Fiscalize company to work in (after a direct sign-in without one, or "All companies" in the top bar).
    path: 'select-company',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/select-company/select-company').then((m) => m.SelectCompany)
  },
  {
    path: 'register-client',
    canActivate: [authGuard, companyGuard],
    loadComponent: () => import('./pages/register-client/register-client').then((m) => m.RegisterClient)
  },
  {
    // A stable address that starts (or restarts) the Xero authorisation.
    // Signed-out users are sent to /login with this as the return URL and land here afterwards.
    path: 'connect',
    redirectTo: 'register-client',
    pathMatch: 'full'
  },
  {
    // Disconnecting is done, with a confirmation, on the connection page.
    path: 'disconnect',
    redirectTo: 'xero-connection',
    pathMatch: 'full'
  },
  {
    path: 'terms',
    loadComponent: () => import('./pages/terms/terms').then((m) => m.Terms)
  },
  {
    path: 'privacy',
    loadComponent: () => import('./pages/privacy/privacy').then((m) => m.Privacy)
  },
  {
    path: 'redirect-url',
    loadComponent: () => import('./pages/oauth-callback/oauth-callback').then((m) => m.OAuthCallback)
  },
  {
    // Fiscalyze's app launcher sends every Direct app to /fiscalize-launch?token=…
    // (the same path every Fiscalize app serves). Keep /launch as an alias.
    path: 'fiscalize-launch',
    loadComponent: () => import('./pages/launch/launch').then((m) => m.LaunchComponent)
  },
  {
    path: 'launch',
    loadComponent: () => import('./pages/launch/launch').then((m) => m.LaunchComponent)
  },
  {
    path: '',
    loadComponent: () => import('./layouts/dashboard-layout/dashboard-layout').then((m) => m.DashboardLayout),
    canActivate: [authGuard, companyGuard],
    children: [
      { path: 'xero-connection', loadComponent: () => import('./pages/xero-connection/xero-connection').then((m) => m.XeroConnection) },

      { path: 'dashboard', canActivate: [setupGuard], loadComponent: () => import('./pages/dashboard/dashboard').then((m) => m.Dashboard) },

      { path: 'invoices', canActivate: [setupGuard], loadComponent: () => import('./pages/invoices/invoices').then((m) => m.Invoices) },
      { path: 'invoices/:id', canActivate: [setupGuard], loadComponent: () => import('./pages/invoice-detail/invoice-detail').then((m) => m.InvoiceDetail) },

      { path: 'credit-notes', canActivate: [setupGuard], loadComponent: () => import('./pages/credit-notes/credit-notes').then((m) => m.CreditNotes) },
      { path: 'credit-notes/:id', canActivate: [setupGuard], loadComponent: () => import('./pages/credit-note-detail/credit-note-detail').then((m) => m.CreditNoteDetail) },

      { path: 'customers', canActivate: [setupGuard], loadComponent: () => import('./pages/customers/customers').then((m) => m.Customers) },
      {
        path: 'customers/:id', canActivate: [setupGuard],
        // Ask before leaving a customer with unsaved buyer details.
        canDeactivate: [(c: { canLeave?: () => boolean | Promise<boolean> }) => c?.canLeave ? c.canLeave() : true],
        loadComponent: () => import('./pages/customer-detail/customer-detail').then((m) => m.CustomerDetail)
      },
      { path: 'items', canActivate: [setupGuard], loadComponent: () => import('./pages/items/items').then((m) => m.Items) },

      { path: 'device-setup', canActivate: [setupGuard], loadComponent: () => import('./pages/device-setup/device-setup').then((m) => m.DeviceSetup) },
      { path: 'fiscalization-settings', canActivate: [setupGuard], loadComponent: () => import('./pages/fiscalization-settings/fiscalization-settings').then((m) => m.FiscalizationSettings) },
      { path: 'webhooks', canActivate: [setupGuard], loadComponent: () => import('./pages/webhooks/webhooks').then((m) => m.Webhooks) }
    ]
  },
  {
    path: '**',
    loadComponent: () => import('./pages/not-found/not-found').then((m) => m.NotFound)
  }
];
