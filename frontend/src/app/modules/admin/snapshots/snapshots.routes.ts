import { Route } from '@angular/router';
import { SnapshotsComponent } from './snapshots.component';
import { SnapshotsHistoryComponent } from './snapshots-history.component';

export const snapshotsRoutes: Route[] = [
    {
        path: 'latest',
        component: SnapshotsComponent,
    },
    {
        path: 'history',
        component: SnapshotsHistoryComponent,
    },
];
