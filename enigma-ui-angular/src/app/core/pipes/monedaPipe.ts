import { CurrencyPipe } from '@angular/common';
import { Divisa } from '../model/divisa';
import { Pipe, PipeTransform } from '@angular/core';
import { AppStateService } from '../services/app-state.service';
import { Observable, map } from 'rxjs';

@Pipe({ name: 'moneda', standalone: true })
export class MonedaPipe implements PipeTransform {
  constructor(private appState: AppStateService) {}
  transform(value: number, moneda: Divisa): Observable<string | null> {
    return this.appState.mostrarBalances.pipe(
      map((mostrar) => {
        const currPipe = new CurrencyPipe('es-AR');
        if (mostrar == 'mostrar') {
          return currPipe.transform(
            value,
            moneda.code,
            'symbol',
            `4.${moneda.exponent}`
          );
        } else
        return currPipe.transform(
          0,
          moneda.code,
          'symbol',
          `4.${moneda.exponent}`
        )!.replaceAll('0', '-',);
      })
    );
  }
}
