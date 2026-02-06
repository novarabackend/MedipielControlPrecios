import { Route } from '@angular/router';
import { SnapshotsComponent } from './snapshots.component';
import { SnapshotsHistoryComponent } from './snapshots-history.component';
import { AlertsComponent } from '../alerts/alerts.component';

export const snapshotsRoutes: Route[] = [
    {
        path: 'latest',
        component: SnapshotsComponent,
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
