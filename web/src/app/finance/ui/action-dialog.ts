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
    [dismissableMask]="false"
    [focusOnShow]="true"
    [focusTrap]="true"
    [blockScroll]="true"
    [appendTo]="'body'"
    [breakpoints]="{ '767px': '100vw' }"
    [style]="{ width: 'min(620px, calc(100vw - 32px))' }"
    [styleClass]="'finance-action-dialog'"
    (visibleChange)="visibilityChanged($event)"
    ><ng-content
  /></p-dialog>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActionDialog {
  @Input() title = '';
  @Input() open = false;
  @Input() dismissible = true;
  @Output() closed = new EventEmitter<void>();

  visibilityChanged(visible: boolean): void {
    if (!visible) this.closed.emit();
  }
}
