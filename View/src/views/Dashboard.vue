<script setup>
import { useRouter } from "vue-router";
import { SwitchButton } from "@element-plus/icons-vue";
import SideMenu from "@/components/SideMenu.vue";
import { clearAuthTokens } from "@/services/auth";
import { ElMessageBox } from "element-plus";

const router = useRouter();

// 退出登录
const handleLogout = () => {
  ElMessageBox.confirm("确定要退出登录吗？", "提示", {
    confirmButtonText: "确定",
    cancelButtonText: "取消",
    type: "warning",
  })
    .then(() => {
      clearAuthTokens();
      router.push("/login");
    })
    .catch(() => {
      // 用户取消
    });
};
</script>

<template>
  <el-container class="app-layout">
    <SideMenu />

    <el-container>
      <el-header class="app-header">
        <div class="header-left">
          <el-text size="large">欢迎回来</el-text>
        </div>
        <div class="header-right">
          <el-dropdown @command="handleLogout">
            <el-avatar size="small" style="cursor: pointer">A</el-avatar>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="logout">
                  <el-icon><SwitchButton /></el-icon>
                  退出登录
                </el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </el-header>

      <el-main class="app-main">
        <router-view />
      </el-main>
    </el-container>
  </el-container>
</template>

<style scoped>
.app-layout {
  height: 100vh;
  background: #f5f7fa;
}

.app-header {
  background: #fff;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid #ebeef5;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 12px;
}

.app-main {
  padding: 24px;
}

.app-card + .app-card {
  margin-top: 16px;
}

.card-title {
  font-weight: 600;
}
</style>
