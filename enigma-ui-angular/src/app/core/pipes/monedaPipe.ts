import { CurrencyPipe } from '@angular/common';
import { Divisa } from '../model/divisa';
import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'moneda', standalone: true})
export class MonedaPipe implements PipeTransform {
   transform(value: number, moneda: Divisa, mostrarBalance:'mostrar'|'ocultar' | null = 'mostrar'): string | null {
    if (mostrarBalance == 'mostrar') {
      const currPipe = new CurrencyPipe('es-AR');
      return currPipe.transform(
        value,
        moneda.code,
        'symbol',
        `4.${moneda.exponent}`
      );
    } else return '$ -.---,--';
  }
}
