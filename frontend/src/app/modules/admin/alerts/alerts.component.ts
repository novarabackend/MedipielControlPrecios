import { CommonModule } from '@angular/common';
import {
    ChangeDetectionStrategy,
    Component,
    computed,
    inject,
    signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { finalize } from 'rxjs/operators';
import {
    AlertItem,
    AlertsService,
} from 'app/core/alerts/alerts.service';

@Component({
    selector: 'app-alerts',
    templateUrl: './alerts.component.html',
    styleUrls: ['./alerts.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [
        CommonModule,
        MatButtonModule,
        MatFormFieldModule,
        MatInputModule,
        MatSelectModule,
    ],
})
export class AlertsComponent {
    private _alertsService = inject(AlertsService);

    readonly loading = signal(false);
    readonly error = signal('');
    readonly items = signal<AlertItem[]>([]);

    readonly filterType = signal('all');
    readonly filterCompetitor = signal('all');
    readonly filterSearch = signal('');

    readonly competitors = computed(() => {
        const map = new Map<number, string>();
        for (const item of this.items()) {
            map.set(item.competitorId, item.competitorName);
        }
        return Array.from(map.entries())
            .map(([id, name]) => ({ id, name }))
            .sort((a, b) => {
                const orderDiff =
                    this.getCompetitorOrder(a.name) - this.getCompetitorOrder(b.name);
                return orderDiff !== 0 ? orderDiff : a.name.localeCompare(b.name);
            });
    });

    readonly filteredItems = computed(() => {
        const type = this.filterType();
        const competitor = this.filterCompetitor();
        const search = this.filterSearch().toLowerCase().trim();

        return this.items().filter((item) => {
            if (type !== 'all' && item.type !== type) {
                return false;
            }
            if (competitor !== 'all' && item.competitorId !== Number(competitor)) {
                return false;
            }
            if (search) {
                const haystack = `${item.productSku ?? ''} ${item.productEan ?? ''} ${item.productDescription}`.toLowerCase();
                if (!haystack.includes(search)) {
                    return false;
                }
            }
            return true;
        });
    });

    constructor() {
        this.load();
    }

    load(): void {
        this.loading.set(true);
        this.error.set('');
        this._alertsService
            .getAlerts()
            .pipe(finalize(() => this.loading.set(false)))
            .subscribe({
                next: (items) => this.items.set(items),
                error: () => this.error.set('No se pudieron cargar las alertas.'),
            });
    }

    private getCompetitorOrder(name: string): number {
        const normalized = (name ?? '').trim().toLowerCase();
        if (normalized.includes('medipiel')) {
            return 0;
        }
        if (normalized.includes('bella piel')) {
            return 1;
        }
        if (normalized.includes('linea estetica')) {
            return 2;
        }
        if (normalized.includes('farmatodo')) {
            return 3;
        }
        if (normalized.includes('cruz verde')) {
            return 4;
        }
        return 99;
    }
}
