import { NgModule } from '@angular/core';
import { IfLoggedInDirective } from './directives/if-logged-in.directive';
import { IfNotLoggedInDirective } from './directives/if-not-logged-in.directive';

@NgModule({
  declarations: [
    IfLoggedInDirective,
    IfNotLoggedInDirective
  ],
  exports: [IfLoggedInDirective, IfNotLoggedInDirective],
})
export class AuthDirectivesModule {}
