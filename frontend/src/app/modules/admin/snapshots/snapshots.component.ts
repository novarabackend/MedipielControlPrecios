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
    deltaListByCompetitor: Record<number, CompetitorDelta>;
    deltaPromoByCompetitor: Record<number, CompetitorDelta>;
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
            const deltaListByCompetitor: Record<number, CompetitorDelta> = {};
            const deltaPromoByCompetitor: Record<number, CompetitorDelta> = {};
            for (const price of row.prices) {
                pricesByCompetitor[price.competitorId] = price;
            }

            const baseListPrice = row.medipielListPrice ?? null;
            const basePromoPrice = row.medipielPromoPrice ?? null;

            for (const competitor of this.competitors()) {
                const price = pricesByCompetitor[competitor.id];
                const listPrice = price?.listPrice;
                const promoPrice = price?.promoPrice;

                if (!price || baseListPrice === null || listPrice === null || listPrice === undefined) {
                    deltaListByCompetitor[competitor.id] = {
                        amount: null,
                        percent: null,
                    };
                } else {
                    const amount = listPrice - baseListPrice;
                    const percent =
                        baseListPrice !== 0 ? (amount / baseListPrice) * 100 : null;
                    deltaListByCompetitor[competitor.id] = {
                        amount,
                        percent,
                    };
                }

                if (!price || basePromoPrice === null || promoPrice === null || promoPrice === undefined) {
                    deltaPromoByCompetitor[competitor.id] = {
                        amount: null,
                        percent: null,
                    };
                } else {
                    const amount = promoPrice - basePromoPrice;
                    const percent =
                        basePromoPrice !== 0 ? (amount / basePromoPrice) * 100 : null;
                    deltaPromoByCompetitor[competitor.id] = {
                        amount,
                        percent,
                    };
                }
            }

            return {
                ...row,
                pricesByCompetitor,
                deltaListByCompetitor,
                deltaPromoByCompetitor,
            };
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
