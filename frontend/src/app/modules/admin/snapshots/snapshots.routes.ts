import { Route } from '@angular/router';
import { SnapshotsHistoryComponent } from './snapshots-history.component';
import { AlertsComponent } from '../alerts/alerts.component';

export const snapshotsRoutes: Route[] = [
    {
        path: '',
        pathMatch: 'full',
        redirectTo: 'history',
    },
    {
        path: 'latest',
        pathMatch: 'full',
        redirectTo: 'history',
    },
    {
        path: 'history',
        component: SnapshotsHistoryComponent,
    },
    {
        path: 'alerts',
        component: AlertsComponent,
    },
];
