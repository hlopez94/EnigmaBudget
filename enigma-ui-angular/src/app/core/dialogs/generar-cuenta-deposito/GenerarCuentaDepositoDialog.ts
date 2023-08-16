import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { GenerarCuentaDepositoComponent } from './generar-cuenta-deposito.component';


@Component({
  selector: 'generar-cuenta-deposito-dialog',
  template: `<button
      mat-icon-button
      aria-label="Cerrar"
      class="mat-card-top-right-menu"
      (click)="onNoClick()">
      <mat-icon>close</mat-icon>
    </button>
    <app-generar-cuenta-deposito (resultado)="resultadoForm($event)">
    </app-generar-cuenta-deposito>`,
  standalone: true,
  imports: [
    GenerarCuentaDepositoComponent,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
  ],
})
export class GenerarCuentaDepositoDialog {
  constructor(
    public dialogRef: MatDialogRef<GenerarCuentaDepositoComponent>
  ) { }

  onNoClick(): void {
    this.dialogRef.close();
  }

  resultadoForm(evento: 'creado' | 'error') {
    if (evento == 'creado')
      this.onNoClick();
  }
}
