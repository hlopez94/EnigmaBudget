import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { DialogModule } from '@openng/optimus-ui/dialog';

@Component({
  selector: 'app-action-dialog',
  standalone: true,
  imports: [DialogModule],
  template: `<p-dialog
    [header]="title"
    [visible]="open"
    [modal]="true"
    [closable]="dismissible"
    [closeOnEscape]="dismissible"
    closeAriaLabel="Cerrar"
    [dismissableMask]="false"
    [focusOnShow]="true"
    [focusTrap]="true"
    [blockScroll]="true"
    [appendTo]="'body'"
    [breakpoints]="{ '767px': '100vw' }"
    [style]="{ width: 'min(620px, calc(100vw - 32px))' }"
    [styleClass]="'finance-action-dialog'"
    (visibleChange)="visibilityChanged($event)"
    (onHide)="restoreFocus()"
    ><ng-content
  /></p-dialog>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActionDialog {
  @Input() title = '';
  @Input()
  set open(value: boolean) {
    if (value && !this.isOpen) {
      const activeElement = document.activeElement;
      this.returnFocusTarget = activeElement instanceof HTMLElement ? activeElement : null;
    }
    this.isOpen = value;
  }
  get open(): boolean {
    return this.isOpen;
  }
  @Input() dismissible = true;
  @Output() closed = new EventEmitter<void>();
  private isOpen = false;
  private returnFocusTarget: HTMLElement | null = null;

  visibilityChanged(visible: boolean): void {
    if (!visible) this.closed.emit();
  }

  restoreFocus(): void {
    if (this.returnFocusTarget?.isConnected) this.returnFocusTarget.focus();
    this.returnFocusTarget = null;
  }
}
