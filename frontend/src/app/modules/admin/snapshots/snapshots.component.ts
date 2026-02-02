import {
    ChangeDetectionStrategy,
    Component,
    computed,
    inject,
    signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { finalize } from 'rxjs/operators';
import {
    LatestSnapshotResponse,
    SnapshotPrice,
    SnapshotRow,
    PriceSnapshotsService,
    CompetitorInfo,
} from 'app/core/price-snapshots/price-snapshots.service';

interface CompetitorDelta {
    amount: number | null;
    percent: number | null;
}

interface SnapshotRowView extends SnapshotRow {
    pricesByCompetitor: Record<number, SnapshotPrice>;
    deltaByCompetitor: Record<number, CompetitorDelta>;
    basePrice: number | null;
}

@Component({
    selector: 'app-snapshots',
    templateUrl: './snapshots.component.html',
    styleUrls: ['./snapshots.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [CommonModule, MatButtonModule, MatIconModule, MatTableModule],
})
export class SnapshotsComponent {
    private _service = inject(PriceSnapshotsService);

    readonly loading = signal(false);
    readonly error = signal('');
    readonly data = signal<LatestSnapshotResponse | null>(null);

    readonly competitors = computed<CompetitorInfo[]>(
        () => this.data()?.competitors ?? []
    );
    readonly rows = computed<SnapshotRow[]>(() => this.data()?.rows ?? []);
    readonly rowsView = computed<SnapshotRowView[]>(() =>
        this.rows().map((row) => {
            const pricesByCompetitor: Record<number, SnapshotPrice> = {};
            const deltaByCompetitor: Record<number, CompetitorDelta> = {};
            for (const price of row.prices) {
                pricesByCompetitor[price.competitorId] = price;
            }

            const basePrice =
                row.medipielPromoPrice ?? row.medipielListPrice ?? null;

            for (const competitor of this.competitors()) {
                const price = pricesByCompetitor[competitor.id];
                if (!price || basePrice === null) {
                    deltaByCompetitor[competitor.id] = {
                        amount: null,
                        percent: null,
                    };
                    continue;
                }
                const competitorPrice = price.promoPrice ?? price.listPrice;
                if (competitorPrice === null || competitorPrice === undefined) {
                    deltaByCompetitor[competitor.id] = {
                        amount: null,
                        percent: null,
                    };
                    continue;
                }
                const amount = competitorPrice - basePrice;
                const percent =
                    basePrice !== 0 ? (amount / basePrice) * 100 : null;
                deltaByCompetitor[competitor.id] = {
                    amount,
                    percent,
                };
            }

            return { ...row, pricesByCompetitor, deltaByCompetitor, basePrice };
        })
    );
    readonly hasItems = computed(() => this.rowsView().length > 0);
    readonly snapshotDate = computed(() => this.data()?.snapshotDate ?? '—');
    readonly colorPalette = [
        '#729fcf',
        '#ffd9b3',
        '#b7d36b',
        '#f3a3a3',
        '#a1c9f1',
    ];

    constructor() {
        this.load();
    }

    load(): void {
        this.loading.set(true);
        this.error.set('');

        this._service
            .getLatest()
            .pipe(finalize(() => this.loading.set(false)))
            .subscribe({
                next: (data) => this.data.set(data),
                error: () => this.error.set('No se pudo cargar el ultimo snapshot.'),
            });
    }

    getCompetitorColor(index: number, competitor: CompetitorInfo): string {
        return competitor.color ?? this.colorPalette[index % this.colorPalette.length];
    }
}
