<template>
  <el-container class="sms-management">
    <el-main>
      <el-card shadow="never" :body-style="{ padding: '20px' }">
        <template #header>
          <div
            style="
              display: flex;
              align-items: center;
              justify-content: space-between;
            "
          >
            <span style="font-size: 18px; font-weight: 600">短信/来电</span>
            <el-tag :type="isConnected ? 'success' : 'info'">
              {{ isConnected ? "已连接" : isConnecting ? "连接中" : "未连接" }}
            </el-tag>
          </div>
        </template>

        <el-form :inline="true" :model="filters" @submit.prevent="handleSearch">
          <el-form-item label="设备ID">
            <el-select
              v-model="filters.deviceId"
              placeholder="请选择设备"
              clearable
              filterable
              :loading="loadingDevices"
              style="width: 180px"
              @change="handleDeviceIdChange"
            >
              <el-option
                v-for="device in deviceOptions"
                :key="device"
                :label="device"
                :value="device"
              />
            </el-select>
          </el-form-item>

          <el-form-item label="COM口" v-if="isAdmin">
            <el-select
              v-model="filters.comPort"
              placeholder="请选择COM口"
              clearable
              filterable
              :loading="loadingDeviceCom"
              :disabled="!filters.deviceId"
              style="width: 180px"
            >
              <el-option
                v-for="com in deviceComData"
                :key="com.portName"
                :label="`${com.portName} ${com.isSmsModem ? '(短信猫)' : ''} ${
                  com.modemInfo?.operator || ''
                }`"
                :value="com.portName"
              />
            </el-select>
          </el-form-item>

          <el-form-item label="号码">
            <el-input
              v-model="filters.number"
              placeholder="请输入号码"
              clearable
              @keyup.enter="handleSearch"
              style="width: 180px"
            />
          </el-form-item>

          <el-form-item label="开始时间">
            <el-date-picker
              v-model="filters.startTime"
              type="datetime"
              placeholder="选择开始时间"
              value-format="YYYY-MM-DDTHH:mm"
              style="width: 200px"
            />
          </el-form-item>

          <el-form-item label="结束时间">
            <el-date-picker
              v-model="filters.endTime"
              type="datetime"
              placeholder="选择结束时间"
              value-format="YYYY-MM-DDTHH:mm"
              style="width: 200px"
            />
          </el-form-item>

          <el-form-item v-if="isAdmin">
            <el-checkbox v-model="includeDeleted" label="显示已删除" />
          </el-form-item>

          <el-form-item>
            <el-checkbox
              v-model="soundEnabled"
              label="提示音"
              @change="saveSoundSetting"
            />
          </el-form-item>

          <el-form-item>
            <el-button type="primary" @click="handleSearch" :icon="Search">
              查询
            </el-button>
            <el-button @click="resetFilters" :icon="Refresh">重置</el-button>
          </el-form-item>
        </el-form>
      </el-card>

      <el-card shadow="never" style="margin-top: 20px">
        <el-tabs v-model="activeTab" @tab-change="handleTabChange">
          <el-tab-pane name="sms">
            <template #label>
              <el-badge
                :value="unreadCounts.sms"
                :hidden="unreadCounts.sms <= 0"
                :max="99"
                type="danger"
              >
                <span>短信记录</span>
              </el-badge>
            </template>

            <el-alert
              v-if="smsError"
              :title="smsError"
              type="error"
              :closable="false"
              style="margin-bottom: 20px"
            />

            <div
              style="
                margin-bottom: 12px;
                display: flex;
                justify-content: flex-end;
                gap: 8px;
              "
            >
              <el-button size="small" @click="refreshUnreadAndList">
                刷新未读
              </el-button>
              <el-button
                size="small"
                type="success"
                @click="markAllRead('Sms')"
              >
                全部已读
              </el-button>
            </div>

            <div v-loading="loading">
              <el-table
                :data="messages"
                stripe
                border
                style="width: 100%"
                :row-class-name="tableRowClassName"
              >
                <el-table-column prop="deviceId" label="设备ID" width="120" />
                <el-table-column prop="comPort" label="COM口" width="100" />
                <el-table-column prop="operator" label="运营商" width="120" />
                <el-table-column
                  prop="senderNumber"
                  label="发件人"
                  width="140"
                />
                <el-table-column
                  prop="messageContent"
                  label="短信内容"
                  min-width="250"
                  show-overflow-tooltip
                />
                <el-table-column
                  prop="receivedTime"
                  label="接收时间"
                  width="180"
                >
                  <template #default="{ row }">
                    {{ formatDate(row.receivedTime) }}
                  </template>
                </el-table-column>

                <el-table-column
                  v-if="isAdmin"
                  label="状态"
                  width="100"
                  align="center"
                >
                  <template #default="{ row }">
                    <el-tag v-if="row.isDelete" type="danger" size="small">
                      已删除
                    </el-tag>
                    <el-tag v-else type="success" size="small">正常</el-tag>
                  </template>
                </el-table-column>

                <el-table-column label="操作" width="180" fixed="right">
                  <template #default="{ row }">
                    <el-button
                      v-if="isAdmin && row.isDelete"
                      type="danger"
                      size="small"
                      @click="hardDelete(row.id)"
                      :icon="Delete"
                    >
                      永久删除
                    </el-button>
                    <el-button
                      v-else-if="isAdmin"
                      type="warning"
                      size="small"
                      @click="softDelete(row.id)"
                      :icon="Delete"
                    >
                      删除
                    </el-button>
                    <el-button
                      v-else-if="!row.isDelete"
                      type="warning"
                      size="small"
                      @click="softDelete(row.id)"
                      :icon="Delete"
                    >
                      删除
                    </el-button>
                    <el-button
                      type="primary"
                      size="small"
                      @click="viewDetail(row)"
                      :icon="View"
                    >
                      详情
                    </el-button>
                    <el-tag
                      v-if="isRowRead('Sms', row)"
                      size="small"
                      type="info"
                      effect="plain"
                    >
                      已读
                    </el-tag>
                    <el-button
                      v-else
                      size="small"
                      type="success"
                      :loading="isMarkReadLoading('Sms', row.id)"
                      :disabled="isMarkReadLoading('Sms', row.id)"
                      @click="markRead('Sms', row.id)"
                    >
                      标记已读
                    </el-button>
                  </template>
                </el-table-column>
              </el-table>

              <el-pagination
                v-model:current-page="pageNumber"
                v-model:page-size="pageSize"
                :total="totalCount"
                :page-sizes="[10, 20, 50, 100]"
                layout="total, sizes, prev, pager, next, jumper"
                @current-change="loadMessages"
                @size-change="loadMessages"
                style="margin-top: 20px; justify-content: center"
              />
            </div>
          </el-tab-pane>

          <el-tab-pane name="hangup">
            <template #label>
              <el-badge
                :value="unreadCounts.hangup"
                :hidden="unreadCounts.hangup <= 0"
                :max="99"
                type="danger"
              >
                <span>来电记录</span>
              </el-badge>
            </template>
            <el-alert
              v-if="hangupError"
              :title="hangupError"
              type="error"
              :closable="false"
              style="margin-bottom: 20px"
            />

            <div
              style="
                margin-bottom: 12px;
                display: flex;
                justify-content: flex-end;
                gap: 8px;
              "
            >
              <el-button size="small" @click="refreshUnreadAndList">
                刷新未读
              </el-button>
              <el-button
                size="small"
                type="success"
                @click="markAllRead('Hangup')"
              >
                全部已读
              </el-button>
            </div>

            <div v-loading="hangupLoading">
              <el-table
                :data="hangupRecords"
                stripe
                border
                style="width: 100%"
                :row-class-name="tableRowClassName"
              >
                <el-table-column prop="deviceId" label="设备ID" width="140" />
                <el-table-column prop="comPort" label="COM口" width="110" />
                <el-table-column
                  prop="callerNumber"
                  label="来电号码"
                  width="150"
                />
                <el-table-column prop="hangupTime" label="挂断时间" width="180">
                  <template #default="{ row }">
                    {{ formatDate(row.hangupTime) }}
                  </template>
                </el-table-column>
                <el-table-column
                  prop="reason"
                  label="原因"
                  width="140"
                  show-overflow-tooltip
                />
                <el-table-column
                  prop="rawLine"
                  label="原始内容"
                  min-width="260"
                  show-overflow-tooltip
                />

                <el-table-column
                  v-if="isAdmin"
                  label="状态"
                  width="100"
                  align="center"
                >
                  <template #default="{ row }">
                    <el-tag v-if="row.isDelete" type="danger" size="small">
                      已删除
                    </el-tag>
                    <el-tag v-else type="success" size="small">正常</el-tag>
                  </template>
                </el-table-column>

                <el-table-column label="操作" width="240" fixed="right">
                  <template #default="{ row }">
                    <el-button
                      v-if="isAdmin && row.isDelete"
                      type="danger"
                      size="small"
                      @click="hardDeleteHangup(row.id)"
                      :icon="Delete"
                    >
                      永久删除
                    </el-button>
                    <el-button
                      v-else-if="isAdmin"
                      type="warning"
                      size="small"
                      @click="softDeleteHangup(row.id)"
                      :icon="Delete"
                    >
                      删除
                    </el-button>
                    <el-button
                      v-else-if="!row.isDelete"
                      type="warning"
                      size="small"
                      @click="softDeleteHangup(row.id)"
                      :icon="Delete"
                    >
                      删除
                    </el-button>

                    <el-tag
                      v-if="isRowRead('Hangup', row)"
                      size="small"
                      type="info"
                      effect="plain"
                    >
                      已读
                    </el-tag>
                    <el-button
                      v-else
                      size="small"
                      type="success"
                      :loading="isMarkReadLoading('Hangup', row.id)"
                      :disabled="isMarkReadLoading('Hangup', row.id)"
                      @click="markRead('Hangup', row.id)"
                    >
                      标记已读
                    </el-button>
                  </template>
                </el-table-column>
              </el-table>

              <el-pagination
                v-model:current-page="hangupPageNumber"
                v-model:page-size="hangupPageSize"
                :total="hangupTotalCount"
                :page-sizes="[10, 20, 50, 100]"
                layout="total, sizes, prev, pager, next, jumper"
                @current-change="loadHangupRecords"
                @size-change="loadHangupRecords"
                style="margin-top: 20px; justify-content: center"
              />
            </div>
          </el-tab-pane>
        </el-tabs>
      </el-card>
    </el-main>

    <el-dialog
      v-model="showDetail"
      title="短信详情"
      width="600px"
      :close-on-click-modal="false"
    >
      <el-descriptions v-if="selectedMessage" :column="1" border>
        <el-descriptions-item label="设备ID">
          {{ selectedMessage.deviceId }}
        </el-descriptions-item>
        <el-descriptions-item label="COM口">
          {{ selectedMessage.comPort }}
        </el-descriptions-item>
        <el-descriptions-item label="运营商">
          {{ selectedMessage.operator || "无" }}
        </el-descriptions-item>
        <el-descriptions-item label="发件人">
          {{ selectedMessage.senderNumber }}
        </el-descriptions-item>
        <el-descriptions-item label="接收时间">
          {{ formatDate(selectedMessage.receivedTime) }}
        </el-descriptions-item>
        <el-descriptions-item label="短信时间戳">
          {{ selectedMessage.smsTimestamp || "无" }}
        </el-descriptions-item>
        <el-descriptions-item label="短信内容">
          <div
            style="
              white-space: pre-wrap;
              word-break: break-word;
              padding: 10px;
              background-color: #f5f7fa;
              border-radius: 4px;
            "
          >
            {{ selectedMessage.messageContent }}
          </div>
        </el-descriptions-item>
        <el-descriptions-item v-if="isAdmin" label="状态">
          <el-tag v-if="selectedMessage.isDelete" type="danger" size="small">
            已删除
          </el-tag>
          <el-tag v-else type="success" size="small">正常</el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="创建时间">
          {{ formatDate(selectedMessage.createTime) }}
        </el-descriptions-item>
        <el-descriptions-item label="更新时间">
          {{ formatDate(selectedMessage.updateTime) }}
        </el-descriptions-item>
        <el-descriptions-item v-if="selectedMessage.remark" label="备注">
          {{ selectedMessage.remark }}
        </el-descriptions-item>
      </el-descriptions>

      <template #footer>
        <el-button @click="closeDetail">关闭</el-button>
      </template>
    </el-dialog>
  </el-container>
</template>

<script>
import { apiRequest } from "../services/api";
import { getComSnapshot, getConnectedDevices } from "../services/device";
import { createDeviceHubConnection } from "@/services/signalr";
import { Search, Refresh, Delete, View } from "@element-plus/icons-vue";
import { ElMessage, ElMessageBox } from "element-plus";

const TAB_SMS = "sms";
const TAB_HANGUP = "hangup";

export default {
  name: "SmsManagement",
  props: {
    defaultTab: {
      type: String,
      default: TAB_SMS,
      validator(value) {
        return value === TAB_SMS || value === TAB_HANGUP;
      },
    },
  },
  components: {
    Search,
    Refresh,
    Delete,
    View,
  },
  data() {
    return {
      activeTab: this.defaultTab === TAB_HANGUP ? TAB_HANGUP : TAB_SMS,

      unreadCounts: {
        sms: 0,
        hangup: 0,
      },
      unreadRefreshTimer: null,

      // 仅用于前端交互：把“刚刚标记已读”的记录变为不可按徽标。
      // 后端目前不返回每条记录的已读状态，因此这里做本地乐观标记。
      localReadState: {
        Sms: {},
        Hangup: {},
      },
      markReadLoadingState: {
        Sms: {},
        Hangup: {},
      },

      messages: [],
      loading: false,
      smsError: null,
      pageNumber: 1,
      pageSize: 20,
      totalCount: 0,

      hangupRecords: [],
      hangupLoading: false,
      hangupError: null,
      hangupPageNumber: 1,
      hangupPageSize: 20,
      hangupTotalCount: 0,

      filters: {
        deviceId: "",
        comPort: "",
        number: "",
        startTime: "",
        endTime: "",
      },
      deviceOptions: [],
      deviceComData: [],
      loadingDevices: false,
      loadingDeviceCom: false,
      includeDeleted: true,
      soundEnabled: true,
      isAdmin: false,
      showDetail: false,
      selectedMessage: null,

      isConnecting: false,
      isConnected: false,
      hubConnection: null,
      smsLiveRefreshTimer: null,
      hangupLiveRefreshTimer: null,
      audioContext: null,
      lastSoundAt: 0,
    };
  },
  mounted() {
    this.checkUserRole();
    this.loadDevices();
    this.loadMessages();
    this.loadHangupRecords();
    this.refreshUnreadCounts();
    this.connect();

    // 浏览器通常需要一次用户交互才能播放声音；这里做一次“解锁”。
    this.setupAudioUnlock();
    this.loadSoundSetting();
  },
  beforeUnmount() {
    this.disconnect();

    if (this.smsLiveRefreshTimer) {
      clearTimeout(this.smsLiveRefreshTimer);
      this.smsLiveRefreshTimer = null;
    }

    if (this.hangupLiveRefreshTimer) {
      clearTimeout(this.hangupLiveRefreshTimer);
      this.hangupLiveRefreshTimer = null;
    }

    if (this.unreadRefreshTimer) {
      clearTimeout(this.unreadRefreshTimer);
      this.unreadRefreshTimer = null;
    }
  },
  methods: {
    loadSoundSetting() {
      try {
        const v = localStorage.getItem("sms_sound_enabled");
        if (v === null) return;
        this.soundEnabled = v === "1";
      } catch (e) {
        // ignore
      }
    },
    saveSoundSetting() {
      try {
        localStorage.setItem(
          "sms_sound_enabled",
          this.soundEnabled ? "1" : "0"
        );
      } catch (e) {
        // ignore
      }
    },
    setupAudioUnlock() {
      // 通过一次用户交互恢复 AudioContext，以满足浏览器自动播放限制。
      if (this.audioContext) return;
      const AudioContextCtor =
        window.AudioContext || /** @type {any} */ (window).webkitAudioContext;
      if (!AudioContextCtor) return;

      this.audioContext = new AudioContextCtor();

      const unlock = async () => {
        try {
          if (this.audioContext && this.audioContext.state === "suspended") {
            await this.audioContext.resume();
          }
        } finally {
          window.removeEventListener("pointerdown", unlock, true);
          window.removeEventListener("keydown", unlock, true);
        }
      };

      window.addEventListener("pointerdown", unlock, true);
      window.addEventListener("keydown", unlock, true);
    },
    async playIncomingSound() {
      if (!this.soundEnabled) return;

      // 简单防抖，避免批量推送时“连响”。
      const now = Date.now();
      if (now - (this.lastSoundAt || 0) < 800) return;
      this.lastSoundAt = now;

      try {
        const audio = new Audio("/sounds/incoming.mp3");
        audio.volume = 0.6;
        audio.currentTime = 0;
        await audio.play();
        return;
      } catch (e) {
        // ignore and fallback
      }

      try {
        const AudioContextCtor =
          window.AudioContext || /** @type {any} */ (window).webkitAudioContext;
        if (!AudioContextCtor) return;

        if (!this.audioContext) {
          this.audioContext = new AudioContextCtor();
        }

        if (this.audioContext.state === "suspended") {
          await this.audioContext.resume();
        }

        const ctx = this.audioContext;
        const o = ctx.createOscillator();
        const g = ctx.createGain();

        o.type = "sine";
        o.frequency.value = 880;

        g.gain.setValueAtTime(0.0001, ctx.currentTime);
        g.gain.exponentialRampToValueAtTime(0.2, ctx.currentTime + 0.01);
        g.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.12);

        o.connect(g);
        g.connect(ctx.destination);
        o.start();
        o.stop(ctx.currentTime + 0.14);
      } catch (e) {
        // ignore
      }
    },
    tryParsePayload(payloadJson) {
      if (!payloadJson) return null;
      try {
        return JSON.parse(payloadJson);
      } catch (error) {
        return null;
      }
    },
    isRealtimeDeviceMatched(deviceId) {
      if (!this.filters.deviceId) return true;
      return String(deviceId) === String(this.filters.deviceId);
    },
    isRealtimeComMatched(payload) {
      if (!this.filters.comPort) return true;
      const comPort = payload?.comPort || payload?.portName;
      if (!comPort) return true;
      return String(comPort) === String(this.filters.comPort);
    },
    isRealtimeNumberMatched(payload, candidateKeys) {
      if (!this.filters.number) return true;
      if (!payload) return true;

      const normalizedFilter = String(this.filters.number).trim();
      if (!normalizedFilter) return true;

      const matched = candidateKeys.some((key) => {
        const value = payload?.[key];
        if (!value) return false;
        return String(value).includes(normalizedFilter);
      });

      // 如果 payload 里没有相关号码字段，默认放行并刷新，避免误丢实时更新。
      const hasAnyCandidate = candidateKeys.some((key) =>
        Boolean(payload?.[key])
      );
      return !hasAnyCandidate || matched;
    },
    scheduleSmsRefresh() {
      if (this.smsLiveRefreshTimer) {
        clearTimeout(this.smsLiveRefreshTimer);
      }

      this.smsLiveRefreshTimer = setTimeout(() => {
        this.loadMessages();
      }, 200);

      this.scheduleUnreadRefresh();
    },
    scheduleHangupRefresh() {
      if (this.hangupLiveRefreshTimer) {
        clearTimeout(this.hangupLiveRefreshTimer);
      }

      this.hangupLiveRefreshTimer = setTimeout(() => {
        this.loadHangupRecords();
      }, 200);

      this.scheduleUnreadRefresh();
    },
    scheduleUnreadRefresh() {
      if (this.unreadRefreshTimer) {
        clearTimeout(this.unreadRefreshTimer);
      }

      // 未读数更新不需要做到 200ms 那么激进；且避免多事件风暴。
      this.unreadRefreshTimer = setTimeout(() => {
        this.refreshUnreadCounts();
      }, 500);
    },
    async refreshUnreadCounts() {
      try {
        const response = await apiRequest("/message-read/unread-counts");
        if (!response.ok) {
          throw new Error("获取未读数失败");
        }

        const data = await response.json();
        this.unreadCounts.sms = Number(data?.sms ?? data?.Sms ?? 0) || 0;
        this.unreadCounts.hangup =
          Number(data?.hangup ?? data?.Hangup ?? 0) || 0;
      } catch (err) {
        // 未读数失败不应该影响主列表。
        console.error("Failed to refresh unread counts:", err);
      }
    },
    async refreshUnreadAndList() {
      await this.refreshUnreadCounts();

      // 同步刷新当前激活 Tab 的列表数据，使 isRead 状态即时更新。
      if (this.activeTab === TAB_SMS) {
        await this.loadMessages();
      } else if (this.activeTab === TAB_HANGUP) {
        await this.loadHangupRecords();
      } else {
        await Promise.all([this.loadMessages(), this.loadHangupRecords()]);
      }
    },
    isRowRead(messageType, row) {
      const type = String(messageType || "").trim();
      const id = row?.id;
      if (!type || !id) return false;

      // 列表数据如果带了 isRead（后端计算），以其为准；否则退回本地乐观状态。
      if (typeof row?.isRead === "boolean") {
        return row.isRead;
      }
      if (typeof row?.IsRead === "boolean") {
        return row.IsRead;
      }

      return Boolean(this.localReadState?.[type]?.[id]);
    },
    isMarkReadLoading(messageType, sourceId) {
      const type = String(messageType || "").trim();
      const id = sourceId;
      if (!type || !id) return false;
      return Boolean(this.markReadLoadingState?.[type]?.[id]);
    },
    async markRead(messageType, sourceId) {
      const type = String(messageType || "").trim();
      if (!type || !sourceId) return;

      if (
        this.isMarkReadLoading(type, sourceId) ||
        this.isRowRead(type, { id: sourceId })
      ) {
        return;
      }

      // 交互上立即禁用，避免重复点击；失败再回滚。
      this.markReadLoadingState[type][sourceId] = true;
      try {
        const response = await apiRequest("/message-read/mark-read", {
          method: "POST",
          body: {
            messageType: type,
            sourceId,
          },
        });

        if (!response.ok) {
          const text = await response.text();
          throw new Error(text || "标记已读失败");
        }

        this.localReadState[type][sourceId] = true;

        // 列表当前行也要立即反映（否则 row.isRead 仍为 false 时会把按钮继续渲染出来）
        if (type === "Sms") {
          const row = this.messages.find((x) => x && x.id === sourceId);
          if (row) {
            row.isRead = true;
          }
        } else if (type === "Hangup") {
          const row = this.hangupRecords.find((x) => x && x.id === sourceId);
          if (row) {
            row.isRead = true;
          }
        }

        ElMessage.success("已标记为已读");
        this.refreshUnreadCounts();
      } catch (err) {
        delete this.localReadState[type][sourceId];
        ElMessage.error("标记已读失败: " + (err?.message || err));
      } finally {
        delete this.markReadLoadingState[type][sourceId];
      }
    },
    async markAllRead(messageType) {
      try {
        const body = {
          messageType,
          deviceId: this.filters.deviceId || null,
          comPort: this.filters.comPort || null,
        };

        const response = await apiRequest("/message-read/mark-all-read", {
          method: "POST",
          body,
        });

        if (!response.ok) {
          const text = await response.text();
          throw new Error(text || "全部已读失败");
        }

        ElMessage.success("已全部标记为已读");

        if (messageType === "Sms") {
          this.loadMessages();
        } else if (messageType === "Hangup") {
          this.loadHangupRecords();
        }

        this.refreshUnreadCounts();
      } catch (err) {
        ElMessage.error("全部已读失败: " + (err?.message || err));
      }
    },
    handleSmsReceived(deviceId, smsDataJson) {
      if (!this.isRealtimeDeviceMatched(deviceId)) {
        return;
      }

      const payload = this.tryParsePayload(smsDataJson);
      if (!this.isRealtimeComMatched(payload)) {
        return;
      }

      if (
        !this.isRealtimeNumberMatched(payload, [
          "senderNumber",
          "phoneNumber",
          "sender",
          "from",
        ])
      ) {
        return;
      }

      this.playIncomingSound();
      this.scheduleSmsRefresh();
    },
    handleCallHangupRecord(deviceId, hangupDataJson) {
      if (!this.isRealtimeDeviceMatched(deviceId)) {
        return;
      }

      const payload = this.tryParsePayload(hangupDataJson);
      if (!this.isRealtimeComMatched(payload)) {
        return;
      }

      if (
        !this.isRealtimeNumberMatched(payload, [
          "callerNumber",
          "phoneNumber",
          "caller",
          "from",
        ])
      ) {
        return;
      }

      this.playIncomingSound();
      this.scheduleHangupRefresh();
    },
    async connect() {
      if (this.isConnected || this.isConnecting) return;

      this.isConnecting = true;
      try {
        this.hubConnection = createDeviceHubConnection();
        this.hubConnection.on("SmsReceived", (deviceId, smsDataJson) => {
          this.handleSmsReceived(deviceId, smsDataJson);
        });
        this.hubConnection.on(
          "CallHangupRecord",
          (deviceId, hangupDataJson) => {
            this.handleCallHangupRecord(deviceId, hangupDataJson);
          }
        );

        await this.hubConnection.start();
        this.isConnected = true;
        ElMessage.success("已连接到即时通讯");
      } catch (error) {
        this.isConnected = false;
        this.hubConnection = null;
        ElMessage.error(`连接失败：${error?.message || error}`);
      } finally {
        this.isConnecting = false;
      }
    },
    async disconnect() {
      if (!this.hubConnection) return;

      try {
        await this.hubConnection.stop();
      } finally {
        this.hubConnection = null;
        this.isConnected = false;
        this.isConnecting = false;
      }
    },
    checkUserRole() {
      try {
        const userStr = localStorage.getItem("sms_user");
        if (userStr) {
          const user = JSON.parse(userStr);
          this.isAdmin = user.role == 1;
        }
      } catch (error) {
        console.error("Failed to check user role:", error);
      }
    },
    async loadDevices() {
      this.loadingDevices = true;
      try {
        const devices = await getConnectedDevices();
        this.deviceOptions = Array.isArray(devices) ? devices : [];
      } catch (error) {
        this.deviceOptions = [];
        ElMessage.error("加载设备列表失败: " + error.message);
      } finally {
        this.loadingDevices = false;
      }
    },
    async loadDeviceComSnapshot(deviceId) {
      if (!deviceId) {
        this.deviceComData = [];
        return;
      }

      this.loadingDeviceCom = true;
      try {
        const snapshot = await getComSnapshot(deviceId);
        this.deviceComData = Array.isArray(snapshot) ? snapshot : [];
      } catch (error) {
        this.deviceComData = [];
        ElMessage.error("加载设备COM信息失败: " + error.message);
      } finally {
        this.loadingDeviceCom = false;
      }
    },
    async handleDeviceIdChange(deviceId) {
      this.filters.comPort = "";
      await this.loadDeviceComSnapshot(deviceId);
    },
    handleSearch() {
      this.pageNumber = 1;
      this.hangupPageNumber = 1;
      this.loadMessages();
      this.loadHangupRecords();
    },
    handleTabChange(tabName) {
      const tab = String(tabName);
      if (tab === TAB_SMS) {
        this.loadMessages();
        return;
      }

      if (tab === TAB_HANGUP) {
        this.loadHangupRecords();
      }
    },
    async loadMessages() {
      this.loading = true;
      this.smsError = null;

      try {
        const params = new URLSearchParams({
          pageNumber: this.pageNumber,
          pageSize: this.pageSize,
        });

        if (this.filters.deviceId) {
          params.append("deviceId", this.filters.deviceId);
        }

        if (this.filters.comPort) {
          params.append("comPort", this.filters.comPort);
        }

        if (this.filters.number) {
          params.append("senderNumber", this.filters.number);
        }

        if (this.filters.startTime) {
          params.append(
            "startTime",
            new Date(this.filters.startTime).toISOString()
          );
        }

        if (this.filters.endTime) {
          params.append(
            "endTime",
            new Date(this.filters.endTime).toISOString()
          );
        }

        let endpoint = "/smsmessages";
        if (this.isAdmin) {
          endpoint = "/smsmessages/admin/all";
          params.append("includeDeleted", this.includeDeleted);
        }

        const response = await apiRequest(`${endpoint}?${params.toString()}`);
        if (!response.ok) {
          throw new Error("Failed to load messages");
        }

        const data = await response.json();
        this.messages = data.data || [];
        this.totalCount = data.totalCount || 0;
      } catch (err) {
        this.smsError = err.message || "加载短信失败";
        console.error("Failed to load messages:", err);
      } finally {
        this.loading = false;
      }
    },
    async loadHangupRecords() {
      this.hangupLoading = true;
      this.hangupError = null;

      try {
        const params = new URLSearchParams({
          pageNumber: this.hangupPageNumber,
          pageSize: this.hangupPageSize,
        });

        if (this.filters.deviceId) {
          params.append("deviceId", this.filters.deviceId);
        }

        if (this.filters.comPort) {
          params.append("comPort", this.filters.comPort);
        }

        if (this.filters.number) {
          params.append("callerNumber", this.filters.number);
        }

        if (this.filters.startTime) {
          params.append(
            "startTime",
            new Date(this.filters.startTime).toISOString()
          );
        }

        if (this.filters.endTime) {
          params.append(
            "endTime",
            new Date(this.filters.endTime).toISOString()
          );
        }

        if (this.isAdmin) {
          params.append("includeDeleted", this.includeDeleted);
        }

        const response = await apiRequest(
          `/call-hangup-records?${params.toString()}`
        );
        if (!response.ok) {
          throw new Error("加载挂断记录失败");
        }

        const data = await response.json();
        this.hangupRecords = data.data || [];
        this.hangupTotalCount = data.totalCount || 0;
      } catch (err) {
        this.hangupError = err?.message || "加载挂断记录失败";
        console.error("Failed to load hangup records:", err);
      } finally {
        this.hangupLoading = false;
      }
    },
    async softDelete(id) {
      try {
        await ElMessageBox.confirm("确定要删除这条短信吗？", "提示", {
          confirmButtonText: "确定",
          cancelButtonText: "取消",
          type: "warning",
        });

        const response = await apiRequest(`/smsmessages/${id}`, {
          method: "DELETE",
        });

        if (!response.ok) {
          throw new Error("Failed to delete message");
        }

        ElMessage.success("删除成功");
        this.loadMessages();
      } catch (err) {
        if (err !== "cancel") {
          ElMessage.error("删除失败: " + err.message);
          console.error("Failed to delete message:", err);
        }
      }
    },
    async hardDelete(id) {
      try {
        await ElMessageBox.confirm(
          "警告：这将永久删除该短信，无法恢复！确定要继续吗？",
          "危险操作",
          {
            confirmButtonText: "确定删除",
            cancelButtonText: "取消",
            type: "error",
          }
        );

        const response = await apiRequest(
          `/smsmessages/admin/hard-delete/${id}`,
          {
            method: "DELETE",
          }
        );

        if (!response.ok) {
          throw new Error("Failed to hard delete message");
        }

        ElMessage.success("永久删除成功");
        this.loadMessages();
      } catch (err) {
        if (err !== "cancel") {
          ElMessage.error("永久删除失败: " + err.message);
          console.error("Failed to hard delete message:", err);
        }
      }
    },
    async softDeleteHangup(id) {
      try {
        await ElMessageBox.confirm("确定要删除这条来电记录吗？", "提示", {
          confirmButtonText: "确定",
          cancelButtonText: "取消",
          type: "warning",
        });

        const response = await apiRequest(`/call-hangup-records/${id}`, {
          method: "DELETE",
        });

        if (!response.ok) {
          throw new Error("Failed to delete hangup record");
        }

        ElMessage.success("删除成功");
        this.loadHangupRecords();
      } catch (err) {
        if (err !== "cancel") {
          ElMessage.error("删除失败: " + err.message);
          console.error("Failed to delete hangup record:", err);
        }
      }
    },
    async hardDeleteHangup(id) {
      try {
        await ElMessageBox.confirm(
          "警告：这将永久删除该来电记录，无法恢复！确定要继续吗？",
          "危险操作",
          {
            confirmButtonText: "确定删除",
            cancelButtonText: "取消",
            type: "error",
          }
        );

        const response = await apiRequest(
          `/call-hangup-records/admin/hard-delete/${id}`,
          {
            method: "DELETE",
          }
        );

        if (!response.ok) {
          throw new Error("Failed to hard delete hangup record");
        }

        ElMessage.success("永久删除成功");
        this.loadHangupRecords();
      } catch (err) {
        if (err !== "cancel") {
          ElMessage.error("永久删除失败: " + err.message);
          console.error("Failed to hard delete hangup record:", err);
        }
      }
    },
    viewDetail(message) {
      this.selectedMessage = message;
      this.showDetail = true;
    },
    closeDetail() {
      this.showDetail = false;
      this.selectedMessage = null;
    },
    resetFilters() {
      this.filters = {
        deviceId: "",
        comPort: "",
        number: "",
        startTime: "",
        endTime: "",
      };

      this.deviceComData = [];
      this.pageNumber = 1;
      this.hangupPageNumber = 1;
      this.loadMessages();
      this.loadHangupRecords();
    },
    tableRowClassName({ row }) {
      return row.isDelete ? "deleted-row" : "";
    },
    formatDate(dateString) {
      if (!dateString) return "-";
      const date = new Date(dateString);
      return date.toLocaleString("zh-CN", {
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
      });
    },
  },
};
</script>

<style scoped>
.deleted-row {
  background-color: #fef0f0 !important;
  opacity: 0.8;
}

:deep(.el-pagination) {
  display: flex;
  justify-content: center;
}
</style>
